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
    [Header("UI References")]
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI joinCodeDisplayText;
    [SerializeField] private GameObject menuPanel;
    
    [Header("Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button disconnectButton;

    private string generatedJoinCode = "";
    private GameObject cachedPlayerPrefab;

    async void Start()
    {
        // 初始UI状态
        UpdateUIState(false);
        
        try 
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            
            statusText.text = "服务已就绪";
        }
        catch (System.Exception e)
        {
            statusText.text = "服务初始化失败";
            Debug.LogError(e);
        }

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
            GameObject localPlayer = Instantiate(cachedPlayerPrefab, new Vector3(0, -5f, 0), Quaternion.identity);
            
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                MouseLook mouseLook = mainCam.GetComponent<MouseLook>();
                if (mouseLook != null)
                {
                    Transform cameraHolder = null;
                    Transform[] allChildren = localPlayer.GetComponentsInChildren<Transform>(true);
                    foreach(var t in allChildren) {
                        if (t.name == "CameraHolderEmpty") {
                            cameraHolder = t;
                            break;
                        }
                    }

                    if (cameraHolder != null)
                    {
                        mouseLook.SetupCamera(localPlayer.transform, cameraHolder);
                        
                        // 清理预制体自带的额外摄像机/AudioListener，防止冲突
                        Camera prefabCam = cameraHolder.GetComponentInChildren<Camera>();
                        if (prefabCam != null && prefabCam != mainCam)
                        {
                            Destroy(prefabCam.gameObject);
                        }
                    }
                }
            }
        }

        // 注册客户端连接回调
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        // 绑定按钮事件
        hostButton.onClick.AddListener(UI_CreateHost);
        joinButton.onClick.AddListener(UI_JoinClient);
        disconnectButton.onClick.AddListener(UI_Disconnect);
    }

    private void Update()
    {
        // 实时检测连接状态更新UI
        bool isConnected = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;
        UpdateUIState(isConnected);
    }

    private void UpdateUIState(bool isConnected)
    {
        hostButton.gameObject.SetActive(!isConnected);
        joinButton.gameObject.SetActive(!isConnected);
        joinCodeInputField.gameObject.SetActive(!isConnected);
        
        disconnectButton.gameObject.SetActive(isConnected);
        joinCodeDisplayText.gameObject.SetActive(isConnected && NetworkManager.Singleton.IsHost);
        
        if (isConnected && NetworkManager.Singleton.IsHost)
        {
            joinCodeDisplayText.text = "Room code: " + generatedJoinCode;
        }
    }

    public async void UI_CreateHost()
    {
        statusText.text = "Now creating room...";
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);
            generatedJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            
            RelayServerData relayServerData = new RelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();
            statusText.text = "Now start host";
            
            // 成功后自动关闭菜单
            FindObjectOfType<GameMenuManager>()?.CloseMenu();
        }
        catch (RelayServiceException e)
        {
            statusText.text = "Failed to create room";
            Debug.LogError(e);
        }
    }

    public async void UI_JoinClient()
    {
        string code = joinCodeInputField.text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            statusText.text = "Please enter room code";
            return;
        }

        statusText.text = "Now join room...";
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);
            RelayServerData relayServerData = new RelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();
            statusText.text = "Now creating room...";
            
            // 成功后自动关闭菜单
            FindObjectOfType<GameMenuManager>()?.CloseMenu();
        }
        catch (RelayServiceException e)
        {
            statusText.text = "Failed to join";
            Debug.LogError(e);
        }
    }

    public void UI_Disconnect()
    {
        NetworkManager.Singleton.Shutdown();
        generatedJoinCode = "";
        statusText.text = "Connection down";
    }

    private void OnClientConnected(ulong clientId)
    {
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
}
