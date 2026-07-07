using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI joinCodeDisplayText;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button disconnectButton;

    private string generatedJoinCode = "";
    private GameObject cachedPlayerPrefab;
    private bool _hasCachedUiState;
    private bool _lastConnected;
    private bool _lastHost;
    private string _lastDisplayedJoinCode = "";

    async void Start()
    {
        // 初始UI状态
        UpdateUIState(false);

        // 缓存玩家预制体并取消自动生成，以便我们手动接管生成逻辑
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig.PlayerPrefab != null)
        {
            cachedPlayerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
            NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
        }

        // 初始状态下先创建一个单机/本地玩家实例，并将悬浮的主摄像机绑定到它身上
        var existingPlayer = FindObjectOfType<PlayerController>();
        if (existingPlayer == null && cachedPlayerPrefab != null)
        {
            // Spawn at a safe height to prevent falling out of world
            GameObject localPlayer = Instantiate(cachedPlayerPrefab, new Vector3(0, 2f, 0), Quaternion.identity);
            
            // Ensure the player is not stuck in the sky due to prefab saved state
            Rigidbody rb = localPlayer.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            
            // Since the prefab is fully working on its own, we do not interfere with its camera hierarchy.
            // We just disable any leftover standalone cameras in the scene to avoid conflicts.
            Camera[] allCams = FindObjectsOfType<Camera>();
            foreach (Camera cam in allCams)
            {
                if (cam != null && cam.transform.root != localPlayer.transform)
                {
                    cam.gameObject.SetActive(false);
                }
            }
        }

        // 注册客户端连接回调
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        // 绑定按钮事件
        if (hostButton != null) hostButton.onClick.AddListener(UI_CreateHost);
        if (joinButton != null) joinButton.onClick.AddListener(UI_JoinClient);
        if (disconnectButton != null) disconnectButton.onClick.AddListener(UI_Disconnect);
        
        try 
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            
            SetTextIfChanged(statusText, "Services ready");
        }
        catch (System.Exception e)
        {
            SetTextIfChanged(statusText, "Services init failed");
            Debug.LogError(e);
        }
    }

    private void UpdateUIState(bool isConnected)
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        SetActiveIfChanged(hostButton != null ? hostButton.gameObject : null, !isConnected);
        SetActiveIfChanged(joinButton != null ? joinButton.gameObject : null, !isConnected);
        SetActiveIfChanged(joinCodeInputField != null ? joinCodeInputField.gameObject : null, !isConnected);

        SetActiveIfChanged(disconnectButton != null ? disconnectButton.gameObject : null, isConnected);
        SetActiveIfChanged(joinCodeDisplayText != null ? joinCodeDisplayText.gameObject : null, isConnected && isHost);

        if (isConnected && isHost)
        {
            SetTextIfChanged(joinCodeDisplayText, "Room code: " + generatedJoinCode);
        }

        _lastConnected = isConnected;
        _lastHost = isHost;
        _lastDisplayedJoinCode = generatedJoinCode;
        _hasCachedUiState = true;
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private static void SetTextIfChanged(TextMeshProUGUI target, string value)
    {
        if (target != null && target.text != value)
            target.text = value;
    }

    public async void UI_CreateHost()
    {
        SetTextIfChanged(statusText, "Now creating room...");
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);
            generatedJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            _hasCachedUiState = false;
            
            RelayServerData relayServerData = new RelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();
            SetTextIfChanged(statusText, "Now start host");
            UpdateUIState(true);
            
            // 成功后自动关闭菜单
            FindObjectOfType<GameMenuManager>()?.CloseMenu();
        }
        catch (RelayServiceException e)
        {
            SetTextIfChanged(statusText, "Failed to create room");
            Debug.LogError(e);
        }
    }

    public async void UI_JoinClient()
    {
        string code = joinCodeInputField.text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            SetTextIfChanged(statusText, "Please enter room code");
            return;
        }

        SetTextIfChanged(statusText, "Now join room...");
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);
            RelayServerData relayServerData = new RelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();
            SetTextIfChanged(statusText, "Now creating room...");
            _hasCachedUiState = false;
            UpdateUIState(true);
            
            // 成功后自动关闭菜单
            FindObjectOfType<GameMenuManager>()?.CloseMenu();
        }
        catch (RelayServiceException e)
        {
            SetTextIfChanged(statusText, "Failed to join");
            Debug.LogError(e);
        }
    }

    public void UI_Disconnect()
    {
        NetworkManager.Singleton.Shutdown();
        generatedJoinCode = "";
        _hasCachedUiState = false;
        SetTextIfChanged(statusText, "Connection down");
        UpdateUIState(false);
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        _hasCachedUiState = false;
        UpdateUIState(true);

        // 只有服务端（房主）有权限分配玩家对象
        if (NetworkManager.Singleton.IsServer)
        {
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                // 是房主自己连接了！不要生成新角色，直接“提拔”场景里现有的单机角色
                var existingPlayer = FindObjectOfType<PlayerController>();
                if (existingPlayer != null)
                {
                    var netObj = existingPlayer.GetComponent<NetworkObject>();
                    if (netObj != null && !netObj.IsSpawned)
                    {
                        netObj.SpawnAsPlayerObject(clientId, true);
                    }
                }
                else
                {
                    Debug.LogError("找不到场景中的 PlayerController，无法提拔为房主！");
                }
            }
            else
            {
                // 是其他客户端连接了！在房主身边生成一个新的角色给他们
                Vector3 spawnPos = Vector3.zero;
                Quaternion spawnRot = Quaternion.identity;

                // 尝试获取房主的位置
                if (NetworkManager.Singleton.LocalClient.PlayerObject != null)
                {
                    Transform hostTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
                    // 在房主旁边偏移一点生成，防止重叠卡死
                    spawnPos = hostTransform.position + hostTransform.right * 1.5f;
                    spawnRot = hostTransform.rotation;
                }

                if (cachedPlayerPrefab != null)
                {
                    GameObject newPlayer = Instantiate(cachedPlayerPrefab, spawnPos, spawnRot);
                    newPlayer.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
                }
                else
                {
                    Debug.LogError("NetworkManager 中没有配置 Player Prefab！");
                }
            }
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        bool isConnected = NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

        if (!isConnected)
        {
            generatedJoinCode = "";
        }

        _hasCachedUiState = false;
        UpdateUIState(isConnected);
    }
}
