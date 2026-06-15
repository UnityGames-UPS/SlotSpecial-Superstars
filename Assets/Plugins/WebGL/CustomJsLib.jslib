mergeInto(LibraryManager.library, {
    RegisterVisibilityChangeListener: function(gameObjectNamePtr) {
      var gameObjectName = UTF8ToString(gameObjectNamePtr);
      console.log('[JS] RegisterVisibilityChangeListener called for GameObject:', gameObjectName);

      function sendFocusToUnity(focused) {
        try {
          var value = focused ? '1' : '0';
          if (typeof SendMessage === 'function') {
            SendMessage(gameObjectName, 'OnFocusChanged', value);
          } else if (typeof unityInstance !== 'undefined' && unityInstance && unityInstance.SendMessage) {
            unityInstance.SendMessage(gameObjectName, 'OnFocusChanged', value);
          } else {
            console.warn('[JS] Unity SendMessage not available for focus change');
          }
          console.log('[JS] Sent focus state to Unity: ' + value);
        } catch (err) {
          console.error('[JS] Error sending focus message to Unity:', err);
        }
      }

      window._unityVisibilityCallback = function() {
        var hidden = document.hidden || document.webkitHidden;
        console.log('[JS] visibilitychange fired. Hidden:', hidden);
        sendFocusToUnity(!hidden);
      };

      window._unityWindowBlurCallback = function() {
        console.log('[JS] window blur fired');
        sendFocusToUnity(false);
      };

      window._unityWindowFocusCallback = function() {
        console.log('[JS] window focus fired');
        sendFocusToUnity(true);
      };

      // Remove before re-adding to avoid duplicates
      document.removeEventListener('visibilitychange',       window._unityVisibilityCallback);
      document.removeEventListener('webkitvisibilitychange', window._unityVisibilityCallback);
      window.removeEventListener('blur',  window._unityWindowBlurCallback);
      window.removeEventListener('focus', window._unityWindowFocusCallback);

      document.addEventListener('visibilitychange',       window._unityVisibilityCallback);
      document.addEventListener('webkitvisibilitychange', window._unityVisibilityCallback);
      window.addEventListener('blur',  window._unityWindowBlurCallback);
      window.addEventListener('focus', window._unityWindowFocusCallback);

      console.log('[JS] Visibility/focus event listeners registered for:', gameObjectName);
    },

    SendLogToReactNative: function (messagePtr) {
        var message = UTF8ToString(messagePtr);
        // console.log('jslib fun : ' + message);
        if (window.ReactNativeWebView) {
          window.ReactNativeWebView.postMessage(message);
        } 
    },

    DetectDeviceType: function() {
      try {
        var ua = navigator.userAgent || '';
        var maxTouch = navigator.maxTouchPoints || 0;
        var isTablet = /(iPad|Tablet)/i.test(ua) ||
                       /Android/i.test(ua) && !/Mobile/i.test(ua) ||
                       (maxTouch > 1 && /Macintosh/.test(ua));
        var isMobile = /(iPhone|iPod|webOS|BlackBerry|IEMobile|Opera Mini)/i.test(ua) ||
                       /Android.*Mobile/i.test(ua);
        return (isTablet || isMobile) ? 1 : 0;
      } catch (err) {
        console.error('[JS] DetectDeviceType error:', err);
        return 0;
      }
    },

    SendPostMessage: function(messagePtr) {
      var message = UTF8ToString(messagePtr);
      console.log('SendReactPostMessage, message sent: ' + message);
      if(window.ReactNativeWebView){
        if(message == "authToken"){
          var injectedObjectJson = window.ReactNativeWebView.injectedObjectJson();
          var injectedObj = JSON.parse(injectedObjectJson);

          window.ReactNativeWebView.postMessage('Injected obj : ' + injectedObjectJson);
          
          var combinedData = JSON.stringify({
              socketURL: injectedObj.socketURL.trim(),
              cookie: injectedObj.token.trim(),
              nameSpace: injectedObj.nameSpace ? injectedObj.nameSpace.trim() : ""
          });

          if (typeof SendMessage === 'function') {
            SendMessage('SocketManager', 'ReceiveAuthToken', combinedData);
          }
        }
        window.ReactNativeWebView.postMessage(message);
      }
      else if(window.parent){
        if(message == "authToken"){
          window.addEventListener('message', function(event){
            if(event.data.type === 'authToken'){
              var combinedData = JSON.stringify({
                  cookie: event.data.cookie,
                  socketURL: event.data.socketURL,
                  nameSpace: event.data && event.data.nameSpace ? event.data.nameSpace : ''
              }); 

              if (typeof SendMessage === 'function') {
                SendMessage('SocketManager', 'ReceiveAuthToken', combinedData);
              }
              else{
                console.log('SendMessage is not a func');
              }
            }
          });
        }
        if(window.parent.dispatchReactUnityEvent != null){
          window.parent.dispatchReactUnityEvent(message);
        }
      }
    }
});
