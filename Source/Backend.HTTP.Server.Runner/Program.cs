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
using Backend.HTTP.Common;
using Backend.HTTP.Server.Initialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UtilPack;
using System.Reflection;

using TWebHostSetup = System.ValueTuple<Microsoft.AspNetCore.Hosting.IWebHost, Microsoft.Extensions.Configuration.IConfiguration, System.Collections.Concurrent.ConcurrentBag<System.String>, System.Collections.Concurrent.ConcurrentDictionary<System.String, Backend.HTTP.Server.Initialization.AuthenticationDataHolderImpl>>;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Backend.HTTP.Server.Runner
{
   using TWebHostSetupInfo = EitherOr<TWebHostSetup, Int32>;

   class Program
   {
      public const Int32 ERROR_INTERNAL_ERROR = 1;
      public const Int32 ERROR_INVALID_SERVER_CONFIG_FILE_LOCATION = 2;

      public const String CONFIGURATION_SERVER_CONFIG_FILE_PATH = "ConfigurationFile";
      public const String CONFIGURATION_WATCH_ASSEMBLIES = "WatchComponentAssemblies";
      public const String CONFIGURATION_SHUTDOWN_SEMAPHORE_NAME = "ShutdownSemaphoreName";
      public const String CONFIGURATION_WATCHABLE_ASSEMBLIES_QUIET_TIME = "WatchableAssembliesQuietTime";
      public const String CONFIGURATION_WATCH_SERVER_CONFIG_FILE = "WatchServerConfigFile";
      public const String CONFIGURATION_SERVER_STATE_FILE_PATH = "RestartStateFilePath";
      public const String CONFIGURATION_RESTART_SEMAPHORE_NAME = "RestartSemaphoreName";

      static async Task<Int32> Main( String[] args )
      {
         var source = new CancellationTokenSource();
         Console.CancelKeyPress += ( s, e ) =>
         {
            e.Cancel = true;
            source.Cancel();
         };

         TWebHostSetupInfo setupInfo;
         try
         {
            setupInfo = await CreateWebHostAsync( args, source );
         }
         catch ( OperationCanceledException )
         {
            setupInfo = -1; // Canceled
         }
         catch ( Exception exc )
         {
            // Some other error
            if ( source.IsCancellationRequested )
            {
               setupInfo = -1;
            }
            else
            {
               Console.Error.WriteLine( String.Format( "Internal error\n: {0}", exc ) );
               setupInfo = -2;
            }
         }

         Int32 retVal;
         if ( setupInfo.IsFirst )
         {

            retVal = await RunServerAsync( source, setupInfo.First );
         }
         else
         {
            retVal = setupInfo.Second;
         }

         Console.WriteLine( "Exiting with code: " + retVal );

         return retVal;
      }

      private static async Task<TWebHostSetupInfo> CreateWebHostAsync(
         String[] args,
         CancellationTokenSource cancelSource
      )
      {
         TWebHostSetupInfo retVal;
         //try
         //{
         var config = new ConfigurationBuilder()
            .AddCommandLine( args )
            .Build();

         var serverConfigFile = System.IO.Path.GetFullPath( config.GetValue<String>( CONFIGURATION_SERVER_CONFIG_FILE_PATH, "ServerConfig.json" ) );
         IConfigurationRoot serverConfig = null;
         try
         {

            serverConfig = new ConfigurationBuilder()
               .AddJsonFile( serverConfigFile )
               .Build();
         }
         catch ( Exception exc )
         {
            // Don't leak exception
            Console.Error.WriteLine( String.Format( "Error when accessing configuration file {0}: {1}", serverConfigFile, exc.Message ) );
         }

         if ( serverConfig == null )
         {
            retVal = ERROR_INVALID_SERVER_CONFIG_FILE_LOCATION;
         }
         else
         {
            var trackAssemblies = config.GetValue<Boolean>( CONFIGURATION_WATCH_ASSEMBLIES, false );
            var trackConfigFile = config.GetValue<Boolean>( CONFIGURATION_WATCH_SERVER_CONFIG_FILE, false );
            var assembliesBag = trackAssemblies || trackConfigFile ? new ConcurrentBag<String>() : null;
            if ( trackConfigFile )
            {
               assembliesBag.Add( serverConfigFile );
            }
            var initInfo = await ServerInitialization.Create(
               serverConfigFile,
               serverConfig,
               trackAssemblies ?
               ( originalPath, actualPath ) =>
               {
                  assembliesBag.Add( originalPath );
               }
            :
               (Action<String, String>) null,
               cancelSource.Token
               );

            if ( ( initInfo.ServerConfig.ResponseCreatorFactories?.Length ?? 0 ) <= 0 )
            {
               Console.Error.WriteLine( "No response creators loaded!" );
            }

            // For some reason, just doing retval = await Server.CreateServerAsync( ... ); does not seem to trigger implicit cast...
            retVal = new TWebHostSetupInfo(
               (await Server.CreateServerAsync( serverConfigFile, initInfo.ServerConfig, cancelSource.Token ),
               config,
               assembliesBag,
               initInfo.AuthenticationDataHolders
               ) );

         }

         //}
         //catch ( Exception exc )
         //{
         //   Console.Error.WriteLine( String.Format( "Internal error: {0}", exc.Message ) );
         //   retVal = ERROR_INTERNAL_ERROR;
         //}
         return retVal;
      }

      private static async Task<Int32> RunServerAsync(
         CancellationTokenSource cancelSource,
         TWebHostSetup setup
         )
      {
         Int32 retVal;
         try
         {
            var tasks = new List<Task<ServerTaskRunResult>>()
            {
               KeepRunningServer(setup.Item1, cancelSource.Token)
            };
            var bgParams = BackgroundCheckTaskParameters.Create( setup );
            if ( bgParams != null )
            {
               tasks.Add( KeepRunningBackgroundChecks( bgParams, cancelSource ) );
            }

            var completedTask = await Task.WhenAny( tasks.ToArray() );
            var authDatas = setup.Item4;
            if (
               completedTask.Result == ServerTaskRunResult.Restart
               )
            {
               cancelSource.Cancel( false );
               String tmpString;
               if ( authDatas.Count > 0
                  && !String.IsNullOrEmpty( tmpString = setup.Item2.GetValue<String>( CONFIGURATION_SERVER_STATE_FILE_PATH, null ) ) )
               {
                  // TODO serialize auth data holders to given file name.
               }

               if (
                  !String.IsNullOrEmpty( tmpString = setup.Item2.GetValue<String>( CONFIGURATION_RESTART_SEMAPHORE_NAME, null ) )
                  && Semaphore.TryOpenExisting( tmpString, out var restartSemaphore )
                  )
               {
                  using ( restartSemaphore )
                  {
                     restartSemaphore.Release();
                  }
               }
            }
            retVal = 0;
         }
         catch ( Exception exc )
         {
            if ( cancelSource.IsCancellationRequested )
            {
               retVal = -1;
            }
            else
            {
               Console.Error.WriteLine( String.Format( "Error while running server:\n {0}", exc ) );
               retVal = -3;
            }
         }

         return retVal;
      }

      private static async Task<ServerTaskRunResult> KeepRunningServer(
         IWebHost server,
         CancellationToken token
         )
      {
         await server.RunAsync( token );
         return ServerTaskRunResult.Shutdown;
      }

      private static async Task<ServerTaskRunResult> KeepRunningBackgroundChecks(
         BackgroundCheckTaskParameters parameters,
         CancellationTokenSource cancelSource
         )
      {
         // TODO this task should actually also clean up auth data holders

         var shutdownSemaphore = parameters.ShutdownSemaphore;

         var currentlyListenedAssemblies = new HashSet<String>();
         var thisRoundAddedAssemblies = new List<String>();
         var globalAddedAssemblies = parameters.AddedAssemblies;
         DateTime? lastWatchableFileChangedTimestamp = null;
         var watchers = new List<FileSystemWatcher>();

         ServerTaskRunResult? retVal = null;

         Boolean ShouldContinueLoop()
         {
            return !cancelSource.IsCancellationRequested && !retVal.HasValue;
         }

         try
         {
            while ( ShouldContinueLoop() )
            {


               try
               {
                  if ( shutdownSemaphore?.WaitOne( 0 ) ?? false )
                  {
                     cancelSource.Cancel();
                  }

                  if ( globalAddedAssemblies != null )
                  {
                     if ( lastWatchableFileChangedTimestamp.HasValue
                        && DateTime.UtcNow - lastWatchableFileChangedTimestamp.Value > parameters.WatchableAssembliesChangeQuietTime
                        )
                     {
                        // Restart
                        retVal = ServerTaskRunResult.Restart;
                     }
                     else
                     {
                        thisRoundAddedAssemblies.Clear();
                        while ( globalAddedAssemblies.TryTake( out var addedAssembly ) )
                        {
                           if ( currentlyListenedAssemblies.Add( addedAssembly = Path.GetFullPath( addedAssembly ) ) )
                           {
                              thisRoundAddedAssemblies.Add( addedAssembly );
                           }
                        }

                        if ( thisRoundAddedAssemblies.Count > 0 )
                        {
                           // New assemblies were discovered, start listening
                           // Group by folder, and extension


                           var pathDictionary = thisRoundAddedAssemblies
                              .GroupBy( path => Path.GetDirectoryName( path ) )
                              .ToDictionary( grp => grp.Key, grp => grp
                                 .GroupBy( path => Path.GetExtension( path ) )
                                 .ToDictionary( grp2 => grp2.Key, grp2 => grp2.ToArray() )
                              );
                           foreach ( var watcher in pathDictionary.SelectMany( kvp =>
                             {

                                return kvp.Value.Select( kvp2 =>
                                {
                                   var extension = kvp2.Key;
                                   var fullPaths = kvp2.Value;
                                   var watcher = new FileSystemWatcher()
                                   {
                                      IncludeSubdirectories = false,
                                      NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.LastAccess | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                                      Path = kvp.Key,
                                      Filter = "*" + extension
                                   };

                                   Action<String> actualHandler = fullPath =>
                                   {
                                      if ( Array.IndexOf( fullPaths, fullPath ) >= 0 )
                                      {
                                         lastWatchableFileChangedTimestamp = DateTime.UtcNow;
                                         // We must disable events - otherwise, when directory is deleted, the file watcher will free the handle it has on directory, causing directory deletion to success
                                         watcher.EnableRaisingEvents = false;
                                      }
                                   };
                                   FileSystemEventHandler changedHandler = ( thisSender, thisArgs ) =>
                                   {
                                      Console.WriteLine( $"Detected {thisArgs.ChangeType} to {thisArgs.FullPath}." );
                                      actualHandler( thisArgs.FullPath );
                                   };

                                   watcher.Changed += changedHandler;
                                   watcher.Created += changedHandler;
                                   watcher.Deleted += changedHandler;
                                   watcher.Renamed += ( thisSender, thisArgs ) =>
                                   {
                                      Console.WriteLine( $"Detected {thisArgs.ChangeType} to {thisArgs.OldFullPath}" );
                                      actualHandler( thisArgs.OldFullPath );
                                   };
                                   watcher.Error += ( thisSender, thisArgs ) =>
                                   {
                                      Console.WriteLine( $"Detected error in file watcher: {thisArgs.GetException()}." );
                                   };

                                   return watcher;
                                } );

                             } ) )
                           {
                              Console.WriteLine( "Added " + watcher.Path + Path.DirectorySeparatorChar + watcher.Filter + " to file watch list." );
                              watcher.EnableRaisingEvents = true;
                              watchers.Add( watcher );
                           }
                        }
                     }
                  }


               }
               catch ( Exception exc )
               {
                  if ( !cancelSource.IsCancellationRequested )
                  {
                     Console.Error.WriteLine( "Exception in background thread: " + exc );
                  }
               }

               if ( ShouldContinueLoop() )
               {
                  // Don't put the delay in the beginning of the loop, since that would slow down initial file watcher start
                  // Put this at the end of the loop, and also behind loop condition check, so we won't wait for nothing when we need to break.
                  await Task.Delay( 1000 );
               }
            }
         }
         finally
         {
            shutdownSemaphore?.DisposeSafely();
         }


         return retVal ?? ServerTaskRunResult.Shutdown;
      }

      private enum ServerTaskRunResult
      {
         Shutdown,
         Restart
      }

      private sealed class BackgroundCheckTaskParameters
      {
         private BackgroundCheckTaskParameters(
            Semaphore shutdownSemaphore,
            ConcurrentBag<String> addedAssemblies,
            TimeSpan watchableAssembliesChangeQuietTime
            )
         {
            this.ShutdownSemaphore = shutdownSemaphore;
            this.AddedAssemblies = addedAssemblies;
            this.WatchableAssembliesChangeQuietTime = watchableAssembliesChangeQuietTime;
         }

         public Semaphore ShutdownSemaphore { get; }

         public ConcurrentBag<String> AddedAssemblies { get; }

         public TimeSpan WatchableAssembliesChangeQuietTime { get; }

         public static BackgroundCheckTaskParameters Create(
            TWebHostSetup hostSetup
            )
         {
            var config = hostSetup.Item2;
            Semaphore shutdownSemaphore = null;
            var semaphoreName = config.GetValue<String>( CONFIGURATION_SHUTDOWN_SEMAPHORE_NAME, null );
            if ( !String.IsNullOrEmpty( semaphoreName ) )
            {
               if ( !Semaphore.TryOpenExisting( semaphoreName, out shutdownSemaphore ) )
               {
                  Console.Error.WriteLine( String.Format( "Shutdown semaphore name specified, but no semaphore could be opened with such name: \"{0}\".", semaphoreName ) );
               }
            }

            var addedAssemblies = hostSetup.Item3;



            BackgroundCheckTaskParameters retVal;
            if ( shutdownSemaphore != null
               || addedAssemblies != null
               )
            {
               retVal = new BackgroundCheckTaskParameters(
                  shutdownSemaphore,
                  addedAssemblies,
                  config.GetValue<TimeSpan>( CONFIGURATION_WATCHABLE_ASSEMBLIES_QUIET_TIME, TimeSpan.FromSeconds( 1 ) )
                  );
            }
            else
            {
               retVal = null;
            }

            return retVal;
         }
      }

   }

}