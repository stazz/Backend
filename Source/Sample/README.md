# Sample demonstrating Backend.HTTP.* projects
This folder contains files and folders which aim to demonstrate on how HTTP-related Backend projects work.

# Initialization

## Automatic
To initialize and start sample, only one command is required: run MSBuild for `InitializeSample.build` in this folder and give target `InitializeSample` as argument, like this (assuming you're in the root directory of this repository):
```
"c:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe" /t:InitializeSample Source\Sample\InitializeSample.build"
```

This command will take care of downloading and installing any missing NuGet packages, and start up server monitor with correct arguments.
If you shut down the server, you can start it up again by running the above MSBuild command.

## Manual
TODO instructions for manual set up.

# Trying out
Once the server has started up (the first startup might be quite slow, as all required packages are downloaded), it should stop at text `Now listening on: https://localhost:5413`.
Then, you can fire up browser and navigate to the `https://localhost:5413/public/` address, and follow instructions on the page to test out the sample.

**Note for Firefox!** Because the sample uses self-signed certificate, you will get a warning. You must click on 'Advanced' and then 'Add exception' in order to proceed.

**Note for Chrome!** Because the sample uses self-signed ceritificate, you must start Chrome with arguments `--ignore-certificate-errors --unsafely-treat-insecure-origin-as-secure=https://localhost:5413 --user-data-dir=/path/to/some/directory`.
Otherwise you will get SSL errors and the sample won't work, since [Service Worker](https://developers.google.com/web/fundamentals/getting-started/primers/service-workers) won't be loaded.

**Note for Edge!** Currently (05.07.2017), Service Workers are not implemented in Edge. Please use Firefox or Chrome instead.

The sample itself demonstrates how authentication aspect of application can be completely implemented by a Service Worker, and also how to put files and operations behind authentication guard.

# How it works
The `InitializeSample.build` will start a process watcher process ([UtilPack.NuGet.ProcessRunner](https://github.com/CometaSolutions/UtilPack/tree/develop/Source/UtilPack.NuGet.ProcessRunner)) and pass arguments to signal the watcher process which package to run and monitor, and that graceful shutdown and restart are supported.
The process watcher will then install the `Backend.HTTP.Server.Runner` NuGet package, and start it up - this will be the actual HTTP server.

The `Backend.HTTP.Server.Runner` process will then read configuration file located in [configuration file](./Config/SampleServerConfig.json), and dynamically load NuGet packages specified in there, along with some connection and certificate information.
The _response creators_, which are described in the configuration file, are the NuGet packages that produce some output for some HTTP request matching their matcher.
Currently, there exists one matcher: regexp-based matcher, which will match the path.
The code in [Sample backend operation](./SampleBackendOperation/Operation.cs) will be run when the URL path is exactly `/operation/SampleBackendOperation`, as specified in the configuration file.
For simplicity's sake, the sample operation is **not** authentication-guarded, as the class in the source file extends `PublicResponseCreator` instead of `AuthenticationGuardedResponseCreator`.
The code in [Sample login provider](./SampleBackendLogin/LoginProvider.cs) contains login provider used by `Backend.HTTP.Common.Login` response creator, and it contains some very simplistic login functionality, as e.g. LDAP login functionality would be too complicated for this sample.

On frontend side, the static files served are in [Static](./Static) directory.
The `public` directory contains files which do not require authentication, whereas `private` directory will require authenticated client in order for the client to see files located there.
The `serviceworker` directory contains the Service Worker JavaScript file, which does not require authentication to load.

The code in [Service Worker file](./Static/serviceworker/sw.js) takes care of authentication aspect of web application, scanning for all requests and injecting seen authentication token (if any) into `X-SampleAuth` HTTP header of the requests.
The `Backend.HTTP.Common.HeaderAuthenticator` module is configured (in the same [configuration file](./Config/SampleServerConfig.json)) to use header of that name as authentication token header.

# Advanced usage
The `Backend.HTTP.Server.Runner` process (if started with correct arguments) will detect any changes done to server [configuration file](./Config/SampleServerConfig.json) and to assemblies of custom NuGet packages loaded by the server.
To test it out, try modifying the server configuration file and see how the window where the server is running will react by printing some monitoring information about process exiting and then restarting.
Another way to try it out is to compile the .csproj files in [SampleBackendOperation](./SampleBackendOperation) or [SampleBackendLogin](./SampleBackendLogin) directories.
The compilation will push the packages and clear local package repository folders for those packages, causing server to detect the deletion and restart itself.
This way, you can develop your web apps quickly and in a more agile style.
