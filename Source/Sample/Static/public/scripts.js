// Common scripts - helper to invoke backend operation.
var sendBackendOpFunc = function(
  method,
  operation,
  parameters,
  onSuccess,
  onError,
  dataType
  ) {
  var url = "/operation/" + operation;
  var isReadOnly = method !== "POST";
  if (isReadOnly && parameters) {
    var query = "";
    for (param in parameters) {
      if (parameters.hasOwnProperty(param)) {
        var paramValue = parameters[param];
        if (typeof paramValue !== "string") {
          paramValue = JSON.stringify(paramValue);
        }
        query += "&" + encodeURIComponent(param) + "=" + encodeURIComponent(paramValue);
      }
    }
    if (query.length > 0) {
      url = url + "?" + query.substr(1);
    }
  }
  
  var headers = new Headers();
  var opts =
  {
    method: method,
    headers: headers,
    mode: "same-origin",
    credentials: 'omit'
  };
  if (!isReadOnly) {
    headers.set("Content-Type", dataType ? dataType : "application/json");
    opts.body = JSON.stringify( parameters );
  }
  
  return window.fetch(new Request( url, opts)).then(
    function(response) {
      var retVal = {};
      if( response && response.status === 200 && response.type === 'basic' ) {
        retVal.response = response;
      } else {
        retVal.error = response;
      }
      
      return retVal;
    },
    function(error) {
      return { error: error};
    });
};