using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

using Accord.Math;
using brainflow;

namespace World
{
    public class WorldPlayer : NetworkBehaviour
    {
        public float maxSpeed = 120f;
        public float acceleration = 20f;
        public float braking = 20f;
        public float rotationSpeed = 40f;
        public float respawnDelay = 3f;

        private Rigidbody rb;
        private MeshCollider carMesh;

        private Quaternion initialRotation;
        private UnityEngine.Vector3 initialPosition;


        public string port = "COM5";
        public bool simulation; // if 1 simulates brain data, if 0 from board
        private double[] filtered;

        private BoardShim boardShim = null;
        private BrainFlowInputParams input_params = null;
        private MLModel concentration = null;
        private static int board_id = 0;
        private int sampling_rate = 0;
        private int[] eeg_channels = null;
        private int[] gyroChannels = null;
        private float concentration_value = 0.0f;
        private float tilt = 0.0f;



        [HideInInspector]
        public bool isRespawning = false;
        public NetworkVariable<UnityEngine.Vector3> Position = new();
        public GameObject cameraPrefab;

        public GameObject startLinePrefab;

        public override void OnNetworkSpawn()
        {
            string carTag = "Car_" + UnityEngine.Random.Range(1, 100);
            if (IsOwner)
            {
                GameObject newCamera = Instantiate(cameraPrefab);
                newCamera.GetComponent<CameraController>().target = transform;

                GameObject newStartLine = Instantiate(startLinePrefab);
                newStartLine.GetComponent<TimeController>().carTag = carTag;
            }

            // Get the Rigidbody component on the car
            rb = GetComponent<Rigidbody>();
            carMesh = GetComponent<MeshCollider>();
            rb.transform.name = carTag;
            rb.interpolation = RigidbodyInterpolation.Interpolate; // Smooth interpolation

            // Store the initial rotation for respawn
            initialRotation = transform.rotation;

            // Store the initial position for respawn
            initialPosition = transform.position;

            var startingPos = new UnityEngine.Vector3(-30f, 0f, -28f);
            SubmitNewPosition(startingPos, true);

            InitialiseBrainFlow();
        }


        void FixedUpdate()
        {
            if (IsOwner)
            {
                // float horizontalInput = tilt;
                // float verticalInput = concentration_value;
                float horizontalInput = Input.GetAxis("Horizontal");
                float verticalInput = Input.GetAxis("Vertical");
                if (NetworkManager.Singleton.IsServer)
                {
                    HandleMovement(horizontalInput, verticalInput);
                }
                else
                {
                    SubmitMovementServerRpc(horizontalInput, verticalInput);
                }
            }
        }

        [ServerRpc]
        void SubmitPositionRequestServerRpc(UnityEngine.Vector3 pos, ServerRpcParams rpcParams = default)
        {
            Position.Value = pos;
        }

        [ServerRpc]
        void SubmitMovementServerRpc(float horizontalInput, float verticalInput, ServerRpcParams rpcParams = default)
        {
            HandleMovement(horizontalInput, verticalInput);
        }

        public void SubmitNewPosition(UnityEngine.Vector3 pos, bool spawn = false)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                if (spawn) transform.position = pos;
                Position.Value = pos;
            }
            else
            {
                SubmitPositionRequestServerRpc(pos);
            }
        }

        void HandleMovement(float horizontalInput, float verticalInput)
        {
            // Debug.Log("Handling Movement timestep: " + Time.fixedDeltaTime + " " + horizontalInput + " " + verticalInput);

            // Calculate acceleration and braking
            float accelerationInput = Mathf.Clamp01(verticalInput);
            float brakingInput = Mathf.Clamp01(-Mathf.Min(0, verticalInput));

            // Apply forces for acceleration and braking
            if (IsGrounded())
            {
                UnityEngine.Vector3 forwardForce = transform.forward * acceleration * accelerationInput;
                rb.AddForce(forwardForce, ForceMode.Acceleration);
                // Debug.Log("Forward force: " + forwardForce);

                // Apply braking force
                if (brakingInput > 0)
                {
                    rb.AddForce(-rb.velocity.normalized * brakingInput * braking, ForceMode.VelocityChange);
                }
            }

            // Clamp the speed
            rb.velocity = UnityEngine.Vector3.ClampMagnitude(rb.velocity, maxSpeed);

            // Filter tilt input for reduced sensitivity near 0
            float filteredTilt = FilterTiltInput(tilt);

            // Rotate the car smoothly based on horizontal input
            Quaternion rotation = Quaternion.Euler(0f, horizontalInput * rotationSpeed * Time.fixedDeltaTime, 0f);
            rb.MoveRotation(rb.rotation * rotation);

            // Check if the car is flipped and initiate respawn
            if (IsCarFlipped() && !isRespawning)
            {
                StartCoroutine(RespawnAfterDelay());
            }

            // Update the position on the server
            SubmitNewPosition(rb.position);
        }

        float FilterTiltInput(float input)
        {
            // Adjust the sensitivity threshold based on your needs
            float sensitivityThreshold = 50f;

            // If input is close to 0, reduce sensitivity
            if (Mathf.Abs(input) < sensitivityThreshold)
            {
                return 0f;
            }
            else
            {
                return input;
            }
        }


        void Update()
        {
            RealTimeBrainflow();
            // if (NetworkManager.Singleton.IsServer)
            // {
            //     transform.position = Position.Value;
            // }
        }

        bool IsCarFlipped()
        {
            // Check if the car is flipped by comparing its up direction with the world up direction
            return UnityEngine.Vector3.Dot(transform.up, UnityEngine.Vector3.up) < 0.0f;
        }

        bool IsGrounded()
        {
            // calculate the distance to the ground
            float distToGround = carMesh.bounds.extents.y;

            // create a downward ray originating from the center of the object
            Ray ray = new(transform.position + (UnityEngine.Vector3.up * distToGround), -UnityEngine.Vector3.up);

            // check if this ray intersects any objects within the specified distance
            // Also, if the car collides with multiple layers, assign the ground layer here
            // to ignore rays hitting non-ground objects.
            return Physics.Raycast(ray, distToGround + 0.1f);
        }

        // ALL BRAINFLOW CODE
        IEnumerator RespawnAfterDelay()
        {
            isRespawning = true;

            yield return new WaitForSeconds(respawnDelay);

            // Reset the car's position and rotation to the initial values
            transform.rotation = initialRotation;

            // Reset the Rigidbody properties
            rb.velocity = UnityEngine.Vector3.zero;
            rb.angularVelocity = UnityEngine.Vector3.zero;

            SubmitNewPosition(initialPosition);

            isRespawning = false;
        }

        public void InitialiseBrainFlow()
        {
            try
            {
                input_params = new BrainFlowInputParams();

                BoardShim.set_log_file("brainflow_log.txt");
                BoardShim.enable_dev_board_logger();

                if (simulation)
                {
                    board_id = (int)BoardIds.SYNTHETIC_BOARD;
                }

                else
                {
                    input_params.serial_port = port;
                    board_id = (int)BoardIds.MUSE_2_BLED_BOARD;
                }

                boardShim = new BoardShim(board_id, input_params);
                boardShim.prepare_session();

                boardShim.start_stream(450000);

                BrainFlowModelParams concentration_params = new BrainFlowModelParams((int)BrainFlowMetrics.MINDFULNESS, (int)BrainFlowClassifiers.DEFAULT_CLASSIFIER);
                concentration = new MLModel(concentration_params);
                concentration.prepare();
                sampling_rate = BoardShim.get_sampling_rate(board_id);
                eeg_channels = BoardShim.get_eeg_channels(board_id, 0);
                gyroChannels = BoardShim.get_gyro_channels(board_id, 1);
                Debug.Log("Brainflow streaming was started");
            }
            catch (BrainFlowError e)
            {
                Debug.Log(e);
            }
        }

        public void RealTimeBrainflow()
        {
            // ----------------------------------------------- //
            // Brain data elements
            // ----------------------------------------------- //

            if ((boardShim == null) || (concentration == null))
            {
                return;
            }

            int number_of_data_points = sampling_rate * 4; // 4 second window is recommended for concentration and relaxation calculations

            double[,] unprocessed_data = boardShim.get_current_board_data(number_of_data_points);
            if (unprocessed_data.GetRow(0).Length < number_of_data_points)
            {
                return; // wait for more data
            }

            double[,] unprocessed_accel_gyro__data = boardShim.get_current_board_data(number_of_data_points, 1);
            if (unprocessed_accel_gyro__data.GetRow(0).Length < number_of_data_points)
            {
                return; // wait for more data
            }

            for (int i = 0; i < gyroChannels.Length; i++)
            {
                filtered = DataFilter.perform_wavelet_denoising(unprocessed_accel_gyro__data.GetRow(gyroChannels[1]), 21, 3);
            }

            // prepare feature vector
            Tuple<double[], double[]> bands = DataFilter.get_avg_band_powers(unprocessed_data, eeg_channels, sampling_rate, true);

            double[] feature_vector = bands.Item1.Concatenate(bands.Item2);

            foreach (double element in concentration.predict(feature_vector))
            {
                Debug.Log("Concentration: " + element); // calc and print concetration level
                concentration_value = (float)element;
            }

            foreach (double element1 in filtered)
            {
                Debug.Log("Gyro: " + element1); // print gyro data
                tilt = (float)element1;
            }
        }

        public void EndBrainFlow()
        {
            if (boardShim != null)
            {
                try
                {
                    // Debug.Log('1');
                    // int nfft = DataFilter.get_nearest_power_of_two (sampling_rate);
                    // Debug.Log('2');

                    boardShim.stop_stream();
                    Debug.Log("Brainflow streaming is being stopped...");

                    // double[,] data = boardShim.get_board_data ();
                    // int[] eeg_channels = board_descr.eeg_channels;
                    // use second channel of synthetic board to see 'alpha'
                    // int channel = eeg_channels[1];

                    boardShim.release_session();
                    concentration.release();

                    // double[] detrend = DataFilter.detrend (data.GetRow (channel), (int)DetrendOperations.LINEAR);
                    // Tuple<double[], double[]> psd = DataFilter.get_psd_welch (detrend, nfft, nfft / 2, sampling_rate, (int)WindowOperations.HANNING);
                    // double band_power_beta = DataFilter.get_band_power (psd, 12.0, 35.0);
                    // double band_power_alpha = DataFilter.get_band_power (psd, 8.0, 12.0);
                    // double band_power_theta = DataFilter.get_band_power (psd, 4.0, 8.0);
                    // double band_power_delta = DataFilter.get_band_power (psd, 0.5, 4.0);
                    // // Debug.Log ("Alpha/Beta Ratio:" + (band_power_alpha / band_power_beta));
                    // Debug.Log(band_power_beta);
                    // Debug.Log(band_power_alpha);
                    // Debug.Log(band_power_theta);
                    // Debug.Log(band_power_delta);                
                }
                catch (BrainFlowError e)
                {
                    Debug.Log(e);
                }
                Debug.Log("Brainflow streaming was stopped");
            }
        }

        new void OnDestroy()
        {
            EndBrainFlow();
        }

    }
}