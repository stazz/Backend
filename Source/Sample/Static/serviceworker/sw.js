// Note - this is "special" Service Worker in such way that it does not do any caching - it only handles authentication aspect of the web application!
console.log('Service worker initializing: ', self);

// Install & Activate from https://davidwalsh.name/service-worker-claim
self.addEventListener('install', function(evt) {
  // `skipWaiting()` forces the waiting ServiceWorker to become the
  // active ServiceWorker, triggering the `onactivate` event.
  // Together with `Clients.claim()` this allows a worker to take effect
  // immediately in the client(s).
  evt.waitUntil(self.skipWaiting());
});
self.addEventListener('activate', function(evt) {
  // `claim()` sets this worker as the active worker for all clients that
	// match the workers scope and triggers an `oncontrollerchange` event for
	// the clients.  
  return self.clients.claim();
});

(function (globalScope) {
  var authToken;
  
  var authTokenRetrievalPromise = undefined;
  function idb_createAuthDBStructure(evt) {
    console.log('Creating auth DB for service worker.');
    // evt is IDBVersionChangeEvent
    evt.target.result.createObjectStore(
      'auth_token',
      {
        autoIncrement: false,
        keyPath: undefined
      });
    console.log('Created auth DB for service worker.');
  }
  
  function idb_onVersionChange(evt) {
    // Remember to close DB when this SW tries to delete it
    evt.target.close();
    authTokenRetrievalPromise = undefined;
  }
  
  function idb_fetchAuthTokenFromPersistentStorage() {
    // Get the auth token from IndexedDB
    var promiseRetVal;
    var dbOpenReq;
    try {
      dbOpenReq = globalScope.indexedDB.open('auth_db', 1);
    } catch (exc) {
      // This happens in e.g. FF private browsing mode
    }
    
    if (dbOpenReq) {
      promiseRetVal = new Promise(function(resolve, reject) {
        dbOpenReq.onsuccess = function(evt) {
          // db is IDBDatabase
          var db = evt.target.result;
        
          db.onversionchange = idb_onVersionChange;
          var authStore = db.transaction(['auth_token'], 'readonly').objectStore('auth_token');
          var getReq = authStore.get('auth_token_key');
        
          getReq.onsuccess = function(storeEvt) {
            var foundAuthToken = storeEvt.target.result;
            if (foundAuthToken && typeof foundAuthToken === 'string') {
              console.log('Found auth token in DB.');
              authToken = foundAuthToken;
            } else {
              console.log('No auth token found in DB - most likely first start or just logged out.');
            }
            resolve(authToken);
          };
          getReq.onerror = function(errorEvt) {
            resolve();
          };
        };
        dbOpenReq.onerror = function(errorEvt) {
          resolve();
        }
        dbOpenReq.onupgradeneeded = idb_createAuthDBStructure;
        dbOpenReq.onblocked = function(evt) {
          // evt is Event
          resolve();
        };
      });
    }
    
    return promiseRetVal || Promise.resolve();
  }
  
  function idb_storeAuthTokenToPersistentStorage(newAuthToken) {
    var promiseRetVal;
    
    var dbOpenReq;
    try {
      dbOpenReq = globalScope.indexedDB.open('auth_db', 1);
    } catch (exc) {
      // This happens in e.g. FF private browsing mode
    }
    
    if (dbOpenReq) {
      promiseRetVal = new Promise(function(resolve, reject) {
        dbOpenReq.onsuccess = function(evt) {
          // db is IDBDatabase
          var db = evt.target.result;
          
          db.onversionchange = idb_onVersionChange;
          var authStore = db.transaction(['auth_token'], 'readwrite').objectStore('auth_token');
          var setReq = authStore.put(newAuthToken, 'auth_token_key' );
          setReq.onsuccess = function(storeEvt) {
            resolve();
          };
          setReq.onerror = function(errorEvt) {
            // Just resolve always
            resolve();
          }
          
        };
        dbOpenReq.onupgradeneeded = idb_createAuthDBStructure;
        dbOpenReq.onblocked = function(evt) {
          // evt is Event
          // TODO
        };
      });
    }
    
    return promiseRetVal || Promise.resolve();
  }
  
  function idb_clearPersistentStorage() {
    try {
      console.log('Clearing service worker auth storage.');
      dbDelReq = globalScope.indexedDB.deleteDatabase('auth_db');
      return new Promise(function(resolve, reject) {
        dbDelReq.onerror = function(evt) {
          console.log('Error in clearing service worker auth storage.', evt);
          resolve();
        };
        
        dbDelReq.onsuccess = function(evt) {
          console.log('Cleared service worker auth storage.');
          resolve();
        };
        dbDelReq.onblocked = function(evt) {
          console.log('Clearing blocked?!');
        };
        dbDelReq.onupgradeneeded = function(evt) {
          console.log('Clearing upgrade needed?!');
        };
      });
      
    } catch (exc) {
      return Promise.resolve();
    }
  }
  
  
  function idb_ensureAuthTokenIsFetched() {
    if (!authTokenRetrievalPromise) {
      // authTokenFetched will reset only on SW update or browser restart (or when we reset it in logout)
      // So we can use variables here as "poor man's almost-persistent storage"
      authTokenRetrievalPromise = idb_fetchAuthTokenFromPersistentStorage();
    }
    return authTokenRetrievalPromise;
  }
  
  var loginURL = self.registration.scope + 'operation/login';
  var logoutURL = self.registration.scope + 'operation/logout';
  var publicURL = self.registration.scope + 'public';
  var thisURL = self.location.href;
  var authHeaderName = "X-SampleAuth";
  function doLogout() {
    // Clear auth token and all stuff from DB
    var seenAuthToken = authToken;
    authToken = undefined;
    authTokenPromise = undefined;
    console.log('SEENENENEN', seenAuthToken);
    return seenAuthToken ? idb_clearPersistentStorage() : Promise.resolve();
  }
  
  globalScope.addEventListener('fetch', function(evt) {
    console.log('FETCH', evt);
    
    var request = evt.request;
    var isLogin = request.url.startsWith( loginURL );
    var isLogout = request.url.startsWith( logoutURL );
    var responsePromise;
    if ( isLogin || isLogout ) {
      //var prePromise;
      var seenAuthToken = authToken;
      if ( isLogout ) {
        // Logout is special - we need to clear auth token and all stuff from DB
        //prePromise = request.text();
        authToken = doLogout();
      } else {
        // Login is special - we need to save auth token in case of successful login
        // Set auth header in case we have old, still working auth token so backend won't need to generate new one
        // Since we are modifying request, we must make a copy of it.
        //prePromise = ;
      }

      responsePromise = request.text().then( function(fd) {
          var opts =
          {
            method: request.method,
            headers: new Headers(request.headers),
            mode: 'same-origin',
            referrer: request.referrer,
            credentials: 'omit'
          };
          if (fd) {
            opts.body = fd;
          }
          var reqCopy = new Request( request.url, opts);
          reqCopy.headers.set(authHeaderName, seenAuthToken || '');
          var afterFetch = self.fetch(reqCopy);
          if ( isLogin ) {
            afterFetch = afterFetch.then(function (response) {
              if ( response && response.status === 200 && response.type === 'basic' ) {
                // Successful login - grab auth token
                // No need to clone - auth token is in headers, and reading them does not use up response.
                var newToken = response.headers.get(authHeaderName);
                if (newToken && seenAuthToken !== newToken && typeof newToken === 'string') {
                  // Don't wait for this promise
                  idb_storeAuthTokenToPersistentStorage(newToken);
                  authToken = newToken;
                }
              }
              
              return response;
            });
          }
          return afterFetch;
      });
    } else {
      // Always wait for auth token fetch from IDB, otherwise fresh navigation from previously closed session will not work.
      // (e.g. logging in, closing browser window, opening browser window, and navigating straight to login-guarded URL)
      // The wait is done only exactly once anyway - the promise is cached, and we will wait for it only for a first fetch during session.
      responsePromise = idb_ensureAuthTokenIsFetched().then( function() {
          var retValPromise;
          if ( !request.url.startsWith( publicURL ) && request.url !== thisURL && authToken ) {
            console.log('SW modifying fetch to include auth token', evt);
            // Need to set our auth-token-header
            retValPromise = request.text().then(function(fd) {
              var opts =
              {
                method: request.method,
                headers: new Headers(request.headers),
                mode: 'same-origin',
                referrer: request.referrer,
                credentials: 'omit'
              };
              if (fd) {
                opts.body = fd;
              }
              var reqCopy = new Request( request.url, opts);
              reqCopy.headers.set(authHeaderName, authToken || '');
              return self.fetch(reqCopy);
            });
          } else {
            // Just send request as-is
            console.log('SW sending fetch as-is', evt);
            retValPromise = self.fetch(request);
          }
          return retValPromise;
        });
    }
    
    evt.respondWith(responsePromise.then(function(response) {
      // If we ever see 401, do logout procedure
      var retVal = response;
      if (response && response.status === 401) {
        retVal = doLogout().then(function() { return response; } );
      }
      return retVal;
    }));
  });
})(self);