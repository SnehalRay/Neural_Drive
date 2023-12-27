using Unity.Netcode;
using UnityEngine;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System;
using Unity.Services.Authentication;
using Unity.Services.Core;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Netcode.Transports.UTP;

namespace World
{
    public class WorldManager : MonoBehaviour
    {

        string playerId = "Not signed in";
        Guid hostAllocationId;
        Guid playerAllocationId;

        string allocationRegion = "";

        string joinCode = "Join Code";

        async void Start()
        {
            await UnityServices.InitializeAsync();
            await OnSignIn();
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 300));
            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                StartButtons();
                joinCode = GUILayout.TextField(joinCode, 20);
            }
            else
            {
                StatusLabels();
            }

            GUILayout.EndArea();
        }

        async void StartButtons()
        {
            if (GUILayout.Button("Host"))
            {
                await SetUpRelay();
            };
            if (GUILayout.Button("Client"))
            {
                await OnJoin();
            }
        }

        void StatusLabels()
        {
            var mode = NetworkManager.Singleton.IsHost ?
                "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client";

            GUILayout.Label("Transport: " +
                NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetType().Name);
            GUILayout.Label("Mode: " + mode);

            GUILayout.Label("Join code " + joinCode);
        }

        public async Task OnSignIn()
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            playerId = AuthenticationService.Instance.PlayerId;

            Debug.Log($"Signed in. Player ID: {playerId}");
        }

        public async Task SetUpRelay()
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(2, null);
            hostAllocationId = allocation.AllocationId;
            allocationRegion = allocation.Region;

            joinCode = await RelayService.Instance.GetJoinCodeAsync(hostAllocationId);


            RelayServerData relayServerData = new(allocation, "dtls");

            Debug.Log(relayServerData.ConnectionData);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();
        }

        public async Task OnJoin()
        {
            Debug.Log("Player - Joining host allocation using join code.");

            try
            {
                var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                playerAllocationId = joinAllocation.AllocationId;
                Debug.Log("Player Allocation ID: " + playerAllocationId);

                RelayServerData relayServerData = new(joinAllocation, "dtls");

                Debug.Log(relayServerData.ConnectionData);

                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

                Debug.Log(NetworkManager.Singleton.GetComponent<UnityTransport>().ConnectionData.Address);

                NetworkManager.Singleton.StartClient();

            }
            catch (RelayServiceException ex)
            {
                Debug.LogError(ex.Message + "\n" + ex.StackTrace);
            }

            return;

        }
    }
}