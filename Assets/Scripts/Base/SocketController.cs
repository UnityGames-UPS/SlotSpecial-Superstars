using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using Best.SocketIO;
using Best.SocketIO.Events;
using DG.Tweening;

public class SocketController : MonoBehaviour
{
  [SerializeField] private GameObject RaycastBlocker;
  [SerializeField] private SlotController SlotManager;
  [SerializeField] private UIManager UiManager;
  internal GameData InitLineBetData = null;
  internal Root InitData;
  internal UiData InitSymbolData = null;
  internal Root ResultData = null;
  internal Player PlayerData = null;
  [SerializeField] internal bool isResultdone = false;
  private SocketManager manager;
  private Socket GameSocket;
  protected string SocketURI = null;
  protected string TestSocketURI = "https://devrealtime.dingdinghouse.com/";
  protected string nameSpace = "playground";
  internal bool SetInit = false;
  private bool isConnected = false;
  private bool hasEverConnected = false;
  [SerializeField] internal JSFunctCalls JSManager;
  [SerializeField] private string testToken;
  internal Action OnInit;
  internal Action ShowDisconnectionPopup;
  private float pingInterval = 2f;
  private bool waitingForPong = false;
  private int missedPongs = 0;
  private const int MaxMissedPongs = 5;
  private Coroutine PingRoutine;
  private string myAuth = null;

  private bool hasFocus = true;
  private float focusLostTime = 0f;
  private Coroutine focusCheckRoutine;
  private float maxBackgroundTime = 60f;
  private bool isExiting = false;
  private bool isBeingDestroyed = false;

  private void Awake()
  {
    SetInit = false;
    Application.runInBackground = true;
  }

  private void Start()
  {
    OpenSocket();
  }

  void ReceiveAuthToken(string jsonData)
  {
    var data = JsonUtility.FromJson<AuthTokenData>(jsonData);
    SocketURI = data.socketURL;
    myAuth = data.cookie;
    nameSpace = data.nameSpace;
  }

  private void OpenSocket()
  {
    SocketOptions options = new SocketOptions();
    options.AutoConnect = false;
    options.Reconnection = false;
    options.Timeout = TimeSpan.FromSeconds(3);
    options.ConnectWith = Best.SocketIO.Transports.TransportTypes.WebSocket;

#if UNITY_WEBGL && !UNITY_EDITOR
    JSManager.SendCustomMessage("authToken");
    StartCoroutine(WaitForAuthToken(options));
#else
    Func<SocketManager, Socket, object> authFunction = (manager, socket) =>
    {
      return new
      {
        token = testToken
      };
    };
    options.Auth = authFunction;
    // Proceed with connecting to the server
    SetupSocketManager(options);
#endif
  }

  private IEnumerator WaitForAuthToken(SocketOptions options)
  {
    while (myAuth == null)
    {
      yield return null;
    }

    Func<SocketManager, Socket, object> authFunction = (manager, socket) =>
    {
      return new
      {
        token = myAuth,
      };
    };
    options.Auth = authFunction;

    SetupSocketManager(options);
  }

  private void SetupSocketManager(SocketOptions options)
  {
#if UNITY_EDITOR
    this.manager = new SocketManager(new Uri(TestSocketURI), options);
#else
    this.manager = new SocketManager(new Uri(SocketURI), options);
#endif

    if (string.IsNullOrEmpty(nameSpace) || string.IsNullOrWhiteSpace(nameSpace))
    {
      GameSocket = this.manager.Socket;
    }
    else
    {
      GameSocket = this.manager.GetSocket("/" + nameSpace);
    }

    GameSocket.On<ConnectResponse>(SocketIOEventTypes.Connect, OnConnected);
    GameSocket.On(SocketIOEventTypes.Disconnect, OnDisconnected);
    GameSocket.On<Error>(SocketIOEventTypes.Error, OnError);
    GameSocket.On<string>("game:init", OnListenEvent);
    GameSocket.On<string>("result", OnListenEvent);
    GameSocket.On<string>("pong", OnPongReceived);

    manager.Open();
  }

  void OnConnected(ConnectResponse resp)
  {
    Debug.Log("✅ Connected to server.");

    if (hasEverConnected)
      UiManager.CheckAndClosePopup();

    isConnected = true;
    hasEverConnected = true;
    waitingForPong = false;
    missedPongs = 0;
    SendPing();
  }

  private void OnDisconnected()
  {
    Debug.LogWarning("⚠️ Disconnected from server.");
    isConnected = false;
    ResetPingRoutine();
    UiManager.DisconnectionPopup();
  }

  private void OnError(Error err)
  {
    Debug.LogError("[ERROR] Socket error: " + err);
    if (!string.IsNullOrEmpty(err.message) && err.message.Contains("Session expired"))
    {
      Debug.LogWarning("Session expired detected");
      OnDisconnected();
#if UNITY_WEBGL && !UNITY_EDITOR
        JSManager.SendCustomMessage("session_expired");
#endif
    }
    else
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JSManager.SendCustomMessage("error");
#endif
    }
  }

  private void OnPongReceived(string data)
  {
    waitingForPong = false;
    missedPongs = 0;
  }

  private void OnListenEvent(string data)
  {
    ParseResponse(data);
  }

  private void SendPing()
  {
    ResetPingRoutine();
    PingRoutine = StartCoroutine(PingCheck());
  }

  void ResetPingRoutine()
  {
    if (PingRoutine != null)
      StopCoroutine(PingRoutine);
    PingRoutine = null;
  }

  private IEnumerator PingCheck()
  {
    while (true)
    {
      if (missedPongs == 0)
        UiManager.CheckAndClosePopup();

      if (waitingForPong)
      {
        if (missedPongs == 2)
          UiManager.ReconnectionPopup();
        missedPongs++;
        if (missedPongs >= MaxMissedPongs)
        {
          isConnected = false;
          UiManager.DisconnectionPopup();
          yield break;
        }
      }
      waitingForPong = true;
      SendData("ping");
      yield return new WaitForSeconds(pingInterval);
    }
  }

  internal void SendData(string eventName, object message = null)
  {
    if (GameSocket == null || !GameSocket.IsOpen)
    {
      Debug.LogWarning("Socket is not connected.");
      return;
    }
    if (message == null)
    {
      GameSocket.Emit(eventName);
      return;
    }

    isResultdone = false;
    string json = JsonConvert.SerializeObject(message);
    GameSocket.Emit(eventName, json);
  }

  void CloseGame()
  {
    StartCoroutine(CloseSocket());
  }

  private void OnDestroy()
  {
    isBeingDestroyed = true;
  }

  internal IEnumerator CloseSocket()
  {
    isExiting = true;
    RaycastBlocker.SetActive(true);
    ResetPingRoutine();

    manager?.Close();
    manager = null;

    yield return new WaitForSeconds(0.5f);

#if UNITY_WEBGL && !UNITY_EDITOR
    JSManager.SendCustomMessage("OnExit");
#endif
  }

  internal void HandleFocusChange(bool focus)
  {
    hasFocus = focus;

    if (!focus)
    {
      focusLostTime = Time.time;
      if (focusCheckRoutine == null && !isExiting && !isBeingDestroyed)
        focusCheckRoutine = StartCoroutine(FocusTimeoutCheck());
    }
    else
    {
      if (focusCheckRoutine != null)
      {
        StopCoroutine(focusCheckRoutine);
        focusCheckRoutine = null;
      }
    }
  }

  private IEnumerator FocusTimeoutCheck()
  {
    while (!hasFocus && !isExiting && !isBeingDestroyed)
    {
      if (Time.time - focusLostTime >= maxBackgroundTime)
      {
        Debug.LogWarning("[SOCKET] Background timeout — closing connection");
        isConnected = false;
        ResetPingRoutine();

        if (manager != null)
        {
          try { manager.Close(); }
          catch (Exception e) { Debug.LogWarning($"[SOCKET] Focus close error: {e.Message}"); }
        }

        UiManager.DisconnectionPopup();
        focusCheckRoutine = null;
        yield break;
      }

      yield return new WaitForSecondsRealtime(1f);
    }

    focusCheckRoutine = null;
  }

  private void ParseResponse(string jsonObject)
  {
    Debug.Log(jsonObject);
    Root myData = JsonConvert.DeserializeObject<Root>(jsonObject);
    PlayerData = myData.player;
    string id = myData.id;

    switch (id)
    {
      case "initData":
        {
          InitLineBetData = myData.gameData;
          InitSymbolData = myData.uiData;
          InitData = myData;

          if (!SetInit)
          {
            OnInit?.Invoke();
            SetInit = true;
            
            PopulateSlotSocket();
          }
          else
          {
            Debug.LogWarning("Received init again");
          }
          break;
        }
      case "ResultData":
        {
          ResultData = myData;
          isResultdone = true;
          break;
        }
    }
  }

  private void PopulateSlotSocket()
  {
    SlotManager.ShuffleMatrix();
    RaycastBlocker.SetActive(false);
    //SlotManager.SetInitialUI();
#if UNITY_WEBGL && !UNITY_EDITOR
        JSManager.SendCustomMessage("OnEnter");
#endif
  }

  internal void AccumulateResult(int currBet)
  {
    isResultdone = false;
    MessageData message = new MessageData();
    message.type = "SPIN";
    message.payload.betIndex = currBet;

    string json = JsonUtility.ToJson(message);
    // SendData("request", message);
    GameSocket.Emit("request", json);
  }
}
