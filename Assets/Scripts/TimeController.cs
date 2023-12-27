using System.Collections;
using Unity.Netcode;
using UnityEngine;



public class TimeController : NetworkBehaviour
{


    private BoxCollider line;

    private float totalElapsedTime = 0f; // Track total elapsed time across laps
    private float lapStartTime;
    private int lapCount = 0;
    private bool timerRunning = false;

    public string carTag;

    private string timerString;


    private NetworkVariable<float> hostLapTime = new(0f);
    private float hLapTime = 0f;
    private NetworkVariable<float> clientLapTime = new(0f);
    private float cLapTime = 0f;


    // Start is called before the first frame update
    void Start()
    {
        // Get the Rigidbody component on the car
        line = GetComponent<BoxCollider>();
        line.tag = "start_line";

        // Initialize lap start time
        lapStartTime = Time.time;
    }

    void OnGUI()
    {

        if ((cLapTime == 0 || hLapTime == 0) && timerRunning)
        {
            float screenWidth = Screen.width;
            float areaWidth = 300;
            float areaHeight = 300;
            // Adjust the Rect to the top right corner
            GUILayout.BeginArea(new Rect(screenWidth - areaWidth - 10, 10, areaWidth, areaHeight));

            // Your GUI elements go here
            GUILayout.Label("Time Elapsed", new GUIStyle() { fontSize = 20 }); // You can set the font size here
            if (timerString != null)
            {
                GUILayout.Label(timerString, new GUIStyle() { fontSize = 15 }); // You can set the font size here
            }

            GUILayout.EndArea();
        }
        else
        {
            if (hLapTime > cLapTime)
            {
                if (NetworkManager.Singleton.IsServer)
                {
                    GUILayout.Label("You Loose!", new GUIStyle() { fontSize = 80, alignment = TextAnchor.MiddleCenter });
                }
                else
                {
                    GUILayout.Label("You Win!", new GUIStyle() { fontSize = 80, alignment = TextAnchor.MiddleCenter });
                }
            }
        }
    }


    // Update is called once per frame
    void StartTimer()
    {
        if (!timerRunning && hLapTime == 0 && cLapTime == 0)
        {
            timerRunning = true;
            lapStartTime = Time.time;
        }
    }

    void StopTimer()
    {
        timerRunning = false;
        float lapTime = Time.time - lapStartTime;
        SubmitLapTime(lapTime);
        totalElapsedTime += lapTime; // Add the lap time to the total elapsed time
        UpdateTimerUI(totalElapsedTime);

        // Add your game-ending logic here
        // For example, you can display a game over message or restart the game.
        // Debug.Log("Game Over! Total Time: " + totalElapsedTime);
        lapCount = 0; // Reset lap count for the next race
        StartCoroutine(EndGameAfterDelay()); // Introduce a delay before ending the game
    }

    void SubmitLapTime(float lapTime)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            hostLapTime.Value = lapTime;
            SetHostLapTimeClientRpc(hLapTime);
        }
        else
        {
            cLapTime = lapTime;
            SubmitLapTimeServerRpc(lapTime);
        }
        Debug.Log("lap times " + lapTime + " " + hLapTime + " " + cLapTime);
    }

    [ClientRpc]
    void SetHostLapTimeClientRpc(float lapTime)
    {
        hLapTime = lapTime;
    }


    [ServerRpc]
    void SubmitLapTimeServerRpc(float lapTime)
    {
        clientLapTime.Value = lapTime;
    }

    void Update()
    {
        if (timerRunning)
        {
            float elapsedTime = Time.time - lapStartTime;
            UpdateTimerUI(elapsedTime);
        }
    }

    void UpdateTimerUI(float lapTime)
    {
        int minutes = Mathf.FloorToInt(lapTime / 60f);
        int seconds = Mathf.FloorToInt(lapTime % 60f);
        int milliseconds = Mathf.FloorToInt((lapTime * 1000) % 1000); // Calculate milliseconds

        timerString = "Lap Time: " + string.Format("{0:00}:{1:00}:{2:000}", minutes, seconds, milliseconds);
    }

    // OnTriggerEnter is called when another collider enters the trigger collider.

    void OnTriggerEnter(Collider other)
    {
        if (other.name == carTag && timerRunning)
        {
            Debug.Log("LAP " + lapCount);
            lapCount++;
            if (lapCount == 1)
            {
                StopTimer(); // Stop the timer after completing one lap
            }
        }
    }


    IEnumerator EndGameAfterDelay()
    {
        yield return new WaitForSeconds(5f); // Adjust the delay time as needed
                                             // Add your game-ending logic here, such as displaying a game over message or restarting the game.
        Debug.Log("Game Over! Total Time: " + totalElapsedTime);
    }

    // OnTriggerExit is called when another collider exits the trigger collider.
    void OnTriggerExit(Collider other)
    {
        // Continue updating the timer even after leaving the trigger
        if (other.name == carTag)
        {
            StartTimer();
        }

    }

}
