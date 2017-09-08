/*
 * Copyright 2017 Stanislav Muhametsin. All rights Reserved.
 *
 * Licensed  under the  Apache License,  Version 2.0  (the "License");
 * you may not use  this file  except in  compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed  under the  License is distributed on an "AS IS" BASIS,
 * WITHOUT  WARRANTIES OR CONDITIONS  OF ANY KIND, either  express  or
 * implied.
 *
 * See the License for the specific language governing permissions and
 * limitations under the License. 
 */
using Microsoft.Extensions.Configuration;
using NuGet.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UtilPack;
using UtilPack.NuGet;
using System.Reflection;
using NuGet.Protocol.Core.Types;
using UtilPack.NuGet.AssemblyLoading;
using NuGet.ProjectModel;
using Backend.Core.Initialization;
using Backend.Core;
using Microsoft.AspNetCore.Http;
using NuGet.Frameworks;
using Backend.HTTP.Common;
using System.Collections.Concurrent;
using System.Threading;
using System.Net;

namespace Backend.HTTP.Server.Initialization
{


   public static class ServerInitialization
   {
      private static readonly Func<AssemblyName, Boolean> LoadUsingParentContext = assemblyName =>
      {
         var simpleName = assemblyName.Name;
         switch ( simpleName )
         {
            case "Backend.HTTP.Common":
               return true;
            default:
               return simpleName.StartsWith( "Microsoft.AspNetCore" );
         }
      };

      public static async Task<(ServerConfiguration ServerConfig, DynamicElementManager<AuthenticatorFactory<HttpContext, HttpRequest, AuthenticationDataHolder>>[] AuthManagers, DynamicElementManager<ResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>>[] ResponseCreatorManagers, ConcurrentDictionary<String, AuthenticationDataHolderImpl> AuthenticationDataHolders)> Create(
         String configurationLocation,
         IConfiguration configuration,
         Action<String, String> onAnyAssemblyLoad,
         CancellationToken token,
         NuGetFramework thisFramework = null,
         InfrastructureConfiguration infraConfig = null,
         SourceCacheContext sourceCacheContext = null,
         LockFile runtimeFrameworkPackages = null
         )
      {

         if ( infraConfig == null )
         {
            infraConfig = configuration.GetSection( "Infrastructure" ).Get<InfrastructureConfiguration>();
            if ( infraConfig != null )
            {
               infraConfig.NuGetConfigurationFile = DynamicElementManagerFactory.ProcessPathValue( configurationLocation, infraConfig.NuGetConfigurationFile );
               infraConfig.DefaultComponentNuGetConfigurationFile = DynamicElementManagerFactory.ProcessPathValue( configurationLocation, infraConfig.DefaultComponentNuGetConfigurationFile );
            }
         }

         if ( thisFramework == null )
         {
            thisFramework = UtilPackNuGetUtility.TryAutoDetectThisProcessFramework( (infraConfig?.NuGetFrameworkID, infraConfig?.NuGetFrameworkVersion) );
         }
         if ( sourceCacheContext == null )
         {
            sourceCacheContext = new SourceCacheContext();
         }

         if ( runtimeFrameworkPackages == null )
         {
            runtimeFrameworkPackages = await GetRuntimeFrameworkPackages( thisFramework: thisFramework, infraConfig: infraConfig, sourceCacheContext: sourceCacheContext );
         }

         // Connection configuration
         var connConfig = configuration
            .GetSection( "Connection" )
            .Get<ConnectionConfiguration>();
         var endPoints = await ProcessEPConfigs( connConfig.EndPoints );

         var defaultNuGetFileLocation = infraConfig?.DefaultComponentNuGetConfigurationFile;

         // Authenticator managers
         var authManagerInfos = configuration
               .GetSection( "Authentication" )
               .GetChildren()
               .Select( authConfigData =>
               {
                  var authConfig = authConfigData.Get<AuthenticationConfiguration>();
                  return (authConfig, authConfigData.GetSection( "Authenticators" ).GetChildren().Select( singleAuthConfig => DynamicElementManagerFactory.CreateFromConfig(
                       configurationLocation,
                       thisFramework,
                       defaultNuGetFileLocation,
                       sourceCacheContext,
                       runtimeFrameworkPackages,
                       singleAuthConfig,
                       resolver => resolver.InstantiateFromConfiguration<AuthenticatorFactory<HttpContext, HttpRequest, AuthenticationDataHolder>>( singleAuthConfig ),
                       LoadUsingParentContext
                       ) ).ToArray());
               } )
               .ToDictionary_Overwrite( t => t.Item1.Schema ?? "", t => t.Item2 );
         if ( onAnyAssemblyLoad != null )
         {
            foreach ( var dynamicInfo in authManagerInfos.SelectMany( kvp => kvp.Value ) )
            {
               dynamicInfo.NuGetAssemblyResolver.OnAssemblyLoadSuccess += args => onAnyAssemblyLoad( args.OriginalPath, args.ActualPath );
            }
         }

         // Instantiate authenticators
         var authDataHolders = new ConcurrentDictionary<String, AuthenticationDataHolderImpl>();
         var authenticators = await Task.WhenAll(
            authManagerInfos
               .Select( async kvp => (kvp.Key, await Task.WhenAll( kvp.Value.Select( async i =>
               {
                  var authFactory = await i.Instance;
                  return authFactory == null ? null : ( await authFactory.CreateAuthenticatorAsync( authDataHolders.GetOrAdd( kvp.Key, schema => new AuthenticationDataHolderImpl() ), token ) );
               } ) )) )
               .ToArray()
            );
         for ( var i = 0; i < authenticators.Length; ++i )
         {
            if ( authenticators[i].Item2 == null || authenticators[i].Item2.Any( a => a == null ) )
            {
               Console.Error.WriteLine( $"Failed to load authenticator at position {i}, please check your configuration." );
            }
         }

         // Create authenticator aggregator
         var serverAuthHandler = new HTTPAuthenticatorAggregator(
            authenticators
               .Where( t => t.Item2 != null )
               .ToDictionary( t => t.Item1, t => t.Item2 )
            );

         // Response creator factory managers

         var responseCreatorManagers = configuration
            .GetSection( "ResponseCreators" )
            .GetChildren()
            .Select( responseCreatorConfig => DynamicElementManagerFactory.CreateFromConfig(
               configurationLocation,
               thisFramework,
               defaultNuGetFileLocation,
               sourceCacheContext,
               runtimeFrameworkPackages,
               responseCreatorConfig,
               resolver => resolver.InstantiateFromConfiguration<ResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>>( responseCreatorConfig ),
               LoadUsingParentContext
               ) )
            .ToArray();

         if ( onAnyAssemblyLoad != null )
         {
            foreach ( var dynamicInfo in responseCreatorManagers )
            {
               dynamicInfo.NuGetAssemblyResolver.OnAssemblyLoadSuccess += args => onAnyAssemblyLoad( args.OriginalPath, args.ActualPath );
            }
         }

         // Response creator factory instances
         var responseCreatorFactories = await Task.WhenAll(
            responseCreatorManagers
               .FilterNulls()
               .Select( async manager => await manager.Instance )
               .ToArray()
            );
         for ( var i = 0; i < responseCreatorFactories.Length; ++i )
         {
            if ( responseCreatorFactories[i] == null )
            {
               Console.Error.WriteLine( $"Failed to load response creator factory at position {i}, please check your configuration." );
            }
         }

         // Can't use ToDictionary_Overwrite directly, since IDictionary does not extend IReadOnlyDictionary
         var epDic = new Dictionary<IPEndPoint, ServerEndPointConfiguration>();
         endPoints.ToDictionary_Overwrite(
                  tuple => tuple.EndPoint,
                  tuple => new ServerEndPointConfigurationImpl(
                     DynamicElementManagerFactory.ProcessPathValue( configurationLocation, tuple.OriginatingConfiguration.X509CertificatePath ),
                     tuple.OriginatingConfiguration.X509CertificatePassword,
                     tuple.OriginatingConfiguration.CheckCertificateRevocation,
                     null // TODO client certificate validation
                     ),
                  dictionaryFactory: eq => epDic
                  );

         // Now we can create the server configuration
         var serverConfig = new ServerConfigurationImpl(
               //DynamicElementManagerFactory.ProcessPathValue( configurationLocation, connConfig.X509CertificatePath ),
               //connConfig.X509CertificatePassword,
               ( options ) =>
               {
                  // This will also set the Limits, because Limits is inside the HTTP section
                  configuration
                     .GetSection( "HTTP" )
                     .Bind( options );
               },
               null,
               epDic,
               serverAuthHandler,
               responseCreatorFactories.FilterNulls().ToArray()
            );

         return (
            serverConfig,
            authManagerInfos
               .SelectMany( kvp => kvp.Value )
               .ToArray(),
            responseCreatorManagers,
            authDataHolders
            );
      }

      public static async Task<LockFile> GetRuntimeFrameworkPackages(
         NuGetFramework thisFramework = null,
         InfrastructureConfiguration infraConfig = null,
         SourceCacheContext sourceCacheContext = null
         )
      {
         using ( var restorer = new BoundRestoreCommandUser(
           DynamicElementManagerFactory.GetNuGetSettings( infraConfig?.NuGetConfigurationFile, null ),
           thisFramework: thisFramework,
           nugetLogger: new TextWriterLogger( new TextWriterLoggerOptions()
           {
              DebugWriter = null
           } ),
           sourceCacheContext: sourceCacheContext,
           leaveSourceCacheOpen: sourceCacheContext != null
           ) )
         {
            var fwPackageID = infraConfig?.NuGetFrameworkPackageID;
            var fwPackageVersion = infraConfig?.NuGetFrameworkPackageVersion;
            if ( String.IsNullOrEmpty( fwPackageID ) )
            {
               fwPackageID = UtilPackNuGetUtility.SDK_PACKAGE_NETCORE;
               fwPackageVersion = "2.0.0";
            }
            return await restorer.RestoreIfNeeded( fwPackageID, fwPackageVersion );
         }
      }

      private static async Task<List<(IPEndPoint EndPoint, EndPointConfiguration OriginatingConfiguration)>> ProcessEPConfigs( EndPointConfiguration[] endPoints )
      {
         if ( endPoints.IsNullOrEmpty() )
         {
            throw new ArgumentException( "Please specify at least one endpoint" );
         }

         var thisAddresses = new AsyncLazy<IPAddress[]>( async () => await Dns.GetHostAddressesAsync( Dns.GetHostName() ) );
         var retVal = new List<(IPEndPoint EndPoint, EndPointConfiguration OriginatingConfiguration)>();
         var cache = new Dictionary<String, Task<IPAddress[]>>();

         IPEndPoint GetIPEndPoint( IPAddress address, Int32 portFromConfig )
         {
            return new IPEndPoint( address, portFromConfig < 0 || portFromConfig > UInt16.MaxValue ?
               443 :
               portFromConfig );
         }

         foreach ( var epConfig in endPoints )
         {
            var curConfig = epConfig;
            String host;
            if ( curConfig != null
               && !String.IsNullOrEmpty( host = curConfig.Host?.Trim() )
               )
            {
               if ( host == "*" )
               {
                  // Asterisk means listen to all addresses of this machine
                  retVal.AddRange( ( await thisAddresses ).Select( addr => (GetIPEndPoint( addr, curConfig.Port ), curConfig) ) );
               }
               else
               {
                  retVal.AddRange( ( await cache.GetOrAdd_NotThreadSafe( host, h => Dns.GetHostAddressesAsync( h ) ) ).Select( addr => (GetIPEndPoint( addr, curConfig.Port ), curConfig) ) );
               }
            }
         }


         return retVal;
      }


   }

   public sealed class AuthenticationDataHolderImpl : AbstractDisposable, AuthenticationDataHolder
   {
      private readonly ConcurrentDictionary<String, AuthenticatedTokenInfo> _authenticationTokens; // Key: authID from request, value: authentication token info
      private readonly IDictionary<String, AuthenticatedUserInfo> _authenticatedUsers; // Key: userID, value: user info
      private readonly Object _lock;
      private readonly Timer _authCleanupTimer;

      public AuthenticationDataHolderImpl()
      {
         this._authenticationTokens = new ConcurrentDictionary<String, AuthenticatedTokenInfo>();
         this._authenticatedUsers = new Dictionary<String, AuthenticatedUserInfo>();
         this._lock = new Object();
         this._authCleanupTimer = new Timer(
            state => this.CleanupAuthData(),
            null,
            TimeSpan.FromMinutes( 1 ),
            Timeout.InfiniteTimeSpan
            );
      }

      public void AddAuthData( String authID, String userID, TimeSpan authIDExpirationTime )
      {
         lock ( this._lock )
         {
            var authUser = this._authenticatedUsers.GetOrAdd_NotThreadSafe( userID, uID => new AuthenticatedUserInfo( new UserInfoImpl( userID ) ) );

            if ( !authUser.AuthTokens.Add( authID ) || !this._authenticationTokens.TryAdd( authID, new AuthenticatedTokenInfo( userID, authIDExpirationTime ) ) )
            {
               throw new Exception( "Duplicate auth ID?" );
            }

         }
      }

      public Boolean TryGetAuthData( String authID, out AuthenticatedTokenInfo authIDInfo, out AuthenticatedUserInfo authUserInfo )
      {
         authIDInfo = null;
         authUserInfo = null;
         if ( !authID.IsNullOrEmpty()
            && this._authenticationTokens.TryGetValue( authID, out authIDInfo )
            )
         {
            // Try avoid locking as much as possible
            if ( this.CheckAuthTokenIsStillValid( authIDInfo ) )
            {
               lock ( this._lock )
               {
                  if ( this._authenticatedUsers.TryGetValue( authIDInfo.UserID, out authUserInfo )
                     && authUserInfo != null )
                  {
                     authIDInfo.MarkAccessedNow();
                  }
               }
            }
            else
            {
               this._authenticationTokens.TryRemove( authID, out authIDInfo );
            }
         }

         return authIDInfo != null && authUserInfo != null;
      }

      public void RemoveAuthData( String authID )
      {
         if ( !authID.IsNullOrEmpty() )
         {
            AuthenticatedTokenInfo authInfo;
            if ( this._authenticationTokens.TryRemove( authID, out authInfo ) )
            {
               lock ( this._lock )
               {
                  AuthenticatedUserInfo authUserInfo;
                  if ( this._authenticatedUsers.TryGetValue( authInfo.UserID, out authUserInfo ) )
                  {
                     authUserInfo.AuthTokens.Remove( authID );
                     if ( authUserInfo.AuthTokens.Count == 0 )
                     {
                        this._authenticatedUsers.Remove( authInfo.UserID );
                        authUserInfo.UserInfo.DisposeSafely();
                     }
                  }
               }

            }
         }
      }

      private void CleanupAuthData()
      {
         try
         {
            lock ( this._lock )
            {
               var authIDsToRemove = this._authenticationTokens
                  .Where( kvp => DateTime.UtcNow - kvp.Value.LastAccessed > kvp.Value.ExpirationSpan )
                  .ToArray();
               foreach ( var kvp in authIDsToRemove )
               {
                  if (
                     this._authenticationTokens.TryRemove( kvp.Key, out var authTokenInfo )
                     && this._authenticatedUsers.TryGetValue( kvp.Value.UserID, out var authUserInfo )
                     )
                  {
                     authUserInfo.AuthTokens.Remove( kvp.Key );
                     if ( authUserInfo.AuthTokens.Count == 0 )
                     {
                        this._authenticatedUsers.Remove( kvp.Value.UserID );
                        authUserInfo.UserInfo.DisposeSafely();
                     }
                  }
               }
            }
         }
         catch
         {
            // Ignore (this is timer callback so don't leak exceptions)
         }
         finally
         {
            // Restart timer
            this._authCleanupTimer.Change( TimeSpan.FromMinutes( 1 ), Timeout.InfiniteTimeSpan );
         }
      }

      private Boolean CheckAuthTokenIsStillValid( AuthenticatedTokenInfo tokenInfo )
      {
         return DateTime.UtcNow - tokenInfo.LastAccessed <= tokenInfo.ExpirationSpan;
      }

      protected override void Dispose( Boolean disposing )
      {
         if ( disposing )
         {
            this._authCleanupTimer.DisposeSafely();
            lock ( this._lock )
            {
               this._authenticatedUsers.Clear();
               this._authenticationTokens.Clear();
            }
         }
      }
   }

   public class UserInfoImpl : AbstractDisposable, UserInfo
   {
      private readonly ConcurrentDictionary<String, Object> _userData;
      private readonly Object _disposeLock;

      public UserInfoImpl(
         String id
      )
      {
         this.ID = id ?? throw new ArgumentNullException( nameof( id ) );
         this._userData = new ConcurrentDictionary<String, Object>();
         this._disposeLock = new Object();
      }

      public String ID { get; }



      public T GetOrAddUserData<T>( String key, Func<String, T> factory )
      {
         void OnDispose()
         {
            if ( this._userData.TryRemove( key, out var val ) )
            {
               DisposeUserData( val );
            }
         }

         Object retVal;
         if ( this.Disposed )
         {
            // This user has already been marked as logged out, dispose data if it exists
            OnDispose();
            retVal = null;
         }
         else if ( !this._userData.TryGetValue( key, out retVal ) )
         {
            lock ( this._disposeLock )
            {
               if ( this.Disposed )
               {
                  OnDispose();
               }
               else if ( !this._userData.TryGetValue( key, out retVal ) )
               {
                  // Now we actually have to create the value
                  this._userData[key] = retVal = factory( key );
               }
            }
         }

         return (T) retVal;
      }

      protected override void Dispose( Boolean disposing )
      {
         if ( disposing )
         {
            var userData = this._userData;
            lock ( this._disposeLock )
            {
               while ( userData.Count > 0 )
               {
                  if ( userData.TryRemove( userData.Keys.FirstOrDefault(), out var data ) )
                  {
                     DisposeUserData( data );
                  }
               }
            }
         }
      }

      private static void DisposeUserData( Object data )
      {
         ( data as IDisposable )?.DisposeSafely();
      }
   }


   public class InfrastructureConfiguration
   {
      public String NuGetConfigurationFile { get; set; }
      public String NuGetFrameworkID { get; set; }
      public String NuGetFrameworkVersion { get; set; }
      public String NuGetFrameworkPackageID { get; set; }
      public String NuGetFrameworkPackageVersion { get; set; }
      public String DefaultComponentNuGetConfigurationFile { get; set; }


   }

   public class AuthenticationConfiguration
   {
      public String Schema { get; set; }

      //public AuthenticatorTypeInfo[] Authenticators { get; set; }
   }

   public class AuthenticatorTypeInfo
   {

   }

   public class ConnectionConfiguration
   {
      public EndPointConfiguration[] EndPoints { get; set; }
   }

   public class EndPointConfiguration
   {
      public String X509CertificatePath { get; set; }
      public String X509CertificatePassword { get; set; }
      public Boolean CheckCertificateRevocation { get; set; }

      public String Host { get; set; } = "localhost";

      public Int32 Port { get; set; } = 443;
   }




}
