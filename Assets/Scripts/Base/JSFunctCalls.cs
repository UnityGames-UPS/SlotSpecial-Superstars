using System.Runtime.InteropServices;
using UnityEngine;

public class JSFunctCalls : MonoBehaviour
{
  [DllImport("__Internal")] private static extern void SendPostMessage(string message);
  [DllImport("__Internal")] private static extern void RegisterVisibilityChangeListener(string gameObjectName);
  [DllImport("__Internal")] private static extern int DetectDeviceType();
  [DllImport("__Internal")] private static extern void RegisterResizeListener(string gameObjectName, string methodName);
  [DllImport("__Internal")] private static extern void RegisterTokenListener(string gameObjectName, string methodName);

  internal static bool IsMobileOrTablet()
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    return DetectDeviceType() == 1;
#else
    return false;
#endif
  }

  // Start, not Awake: the receiver's Awake must run before the initial dimensions callback.
  void Start()
  {
    RegisterDimensionsListener();
  }

  internal void RegisterVisibilityListener(string gameObjectName)
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    Debug.Log($"[JS] Registering visibility change listener on '{gameObjectName}'");
    RegisterVisibilityChangeListener(gameObjectName);
#else
    Debug.Log("[JS] Visibility listener not registered (editor mode)");
#endif
  }

  internal void SendCustomMessage(string message)
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    SendPostMessage(message);
#endif
  }

  // Self-contained resize bridge: the page drives OC.SwitchDisplay("width,height") on its own resize.
  internal void RegisterDimensionsListener(string gameObjectName = "OC", string methodName = "SwitchDisplay")
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    RegisterResizeListener(gameObjectName, methodName);
#else
    Debug.Log($"[JS] Resize listener not registered ('{gameObjectName}.{methodName}', editor mode)");
#endif
  }

  // Inbound auth: routes the host's "TokenReceived" message to gameObjectName.methodName(json).
  internal void RegisterAuthTokenListener(string gameObjectName, string methodName = "ReceiveAuthToken")
  {
#if UNITY_WEBGL && !UNITY_EDITOR
    RegisterTokenListener(gameObjectName, methodName);
#else
    Debug.Log($"[JS] Token listener not registered ('{gameObjectName}.{methodName}', editor mode)");
#endif
  }
}
