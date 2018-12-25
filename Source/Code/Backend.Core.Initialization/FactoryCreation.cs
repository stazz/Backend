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
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGetUtils.Lib.AssemblyResolving;
using NuGetUtils.Lib.Common;
using NuGetUtils.Lib.Restore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UtilPack;
using UtilPack.Configuration;

namespace Backend.Core.Initialization
{

   public class DynamicElementManager<T> : IDisposable
   {
      private readonly BoundRestoreCommandUser _restorer;
      private readonly System.Runtime.Loader.AssemblyLoadContext _loader;

      public DynamicElementManager(
         BoundRestoreCommandUser restorer,
         NuGetAssemblyResolver resolver,
         System.Runtime.Loader.AssemblyLoadContext loader,
         Func<Task<T>> factory
         )
      {
         this._restorer = restorer;
         this.NuGetAssemblyResolver = resolver;
         this._loader = loader;
         this.Instance = new AsyncLazy<T>( factory );
      }

      public AsyncLazy<T> Instance { get; }

      public void Dispose()
      {
         this.NuGetAssemblyResolver.DisposeSafely();
         this._restorer.DisposeSafely();
      }

      public NuGetAssemblyResolver NuGetAssemblyResolver { get; }
   }

   public static class DynamicElementManagerFactory
   {
      public static DynamicElementManager<T> CreateFromConfig<T>(
         String configurationLocation,
         NuGetFramework thisFramework,
         String defaultNuGetConfigFileLocation,
         SourceCacheContext sourceCacheContext,
         LocalPackageFileCache localPackageFileCache,
         LockFile runtimeFrameworkPackages,
         String runtimeID,
         IConfiguration configuration,
         Func<NuGetAssemblyResolver, Task<T>> creator,
         Func<AssemblyName, Boolean> loadAssemblyFromParentContext
         )
      {
         var globaltempFolder = Path.GetTempPath();
         var thisTempFolder = Path.Combine( globaltempFolder, "BackendComponent_" + Guid.NewGuid() );
         Directory.CreateDirectory( thisTempFolder );

         var restorer = new BoundRestoreCommandUser(
            GetNuGetSettings( ProcessPathValue( configurationLocation, configuration.GetSection( "NuGetConfigurationFile" ).Get<String>() ), defaultNuGetConfigFileLocation ),
            thisFramework: thisFramework,
            runtimeIdentifier: runtimeID,
            nugetLogger: new TextWriterLogger( new TextWriterLoggerOptions()
            {
               DebugWriter = null
            } ),
            sourceCacheContext: sourceCacheContext,
            nuspecCache: localPackageFileCache
            );
         var resolver = NuGetAssemblyResolverFactory.NewNuGetAssemblyResolver(
            restorer,
            out var loader,
            thisFrameworkRestoreResult: runtimeFrameworkPackages,
            pathProcessor: originalPath =>
            {
               var actualPath = Path.Combine( thisTempFolder, Path.GetFileName( originalPath ) );
               File.Copy( originalPath, actualPath, false );
               return actualPath;
            },
            additionalCheckForDefaultLoader: assemblyName =>
            {
               var simpleName = assemblyName.Name;
               switch ( simpleName )
               {
                  case "Backend.Core":
                  case "UtilPack.Configuration": // In order to make sure stuff like [ConfigurationType] work.
                  case "System.ValueTuple":
                     return true;
                  default:
                     return ( loadAssemblyFromParentContext?.Invoke( assemblyName ) ?? false )
                     || simpleName.StartsWith( "Microsoft.Extensions." );
               }
            }
            );
         return new DynamicElementManager<T>(
            restorer,
            resolver,
            loader,
            () => creator( resolver )
            );
      }

      public static ISettings GetNuGetSettings(
         String thisConfigFileLocation,
         String defaultConfigFileLocation
         )
      {
         ISettings nugetSettings;
         if ( String.IsNullOrEmpty( thisConfigFileLocation ) && String.IsNullOrEmpty( thisConfigFileLocation = defaultConfigFileLocation ) )
         {
            nugetSettings = Settings.LoadDefaultSettings( Path.GetDirectoryName( new Uri( typeof( DynamicElementManagerFactory ).GetTypeInfo().Assembly.CodeBase ).LocalPath ), null, new XPlatMachineWideSetting() );
         }
         else
         {
            var fp = Path.GetFullPath( thisConfigFileLocation );
            nugetSettings = Settings.LoadSpecificSettings( Path.GetDirectoryName( fp ), Path.GetFileName( fp ) );
         }

         return nugetSettings;
      }


      public static String ProcessPathValue( String configurationLocation, String path )
      {
         if ( !String.IsNullOrEmpty( path ) )
         {
            if ( !Path.IsPathRooted( path ) )
            {
               path = Path.GetFullPath( Path.Combine( Path.GetDirectoryName( configurationLocation ), path ) );
            }
         }

         return path;
      }
   }

   public static class BackendInitializationExtensions
   {
      internal const String PACKAGE_ID = "PackageID";
      internal const String PACKAGE_VERSION = "PackageVersion";
      internal const String ASSEMBLY_PATH = "AssemblyPath";
      internal const String TYPE_NAME = "TypeName";

      public static async Task<T> InstantiateFromConfiguration<T>(
         this NuGetAssemblyResolver resolver,
         IConfiguration configuration,
         CancellationToken token,
         String typeConfigName = "Type"
         )
         where T : class
      {
         var instanceConfig = configuration.GetSection( typeConfigName ).TryGetNuGetPackageConfiguration();
         T retVal = null;
         if ( instanceConfig.HasValue )
         {
            // Collect all other nuget package configs
            var subPackages = configuration
               .GetChildren()
               .Where( c => !String.Equals( typeConfigName, c.Key ) )
               .SelectMany( c => c.GetAllNuGetPackages() )
               .ToArray();

            var allPackageInfo = instanceConfig.Value
               .Singleton()
               .Concat( subPackages )
               .Select( t => (t.PackageID, t.PackageVersion, t.AssemblyPath) )
               .ToArray();
            var packagesLength = allPackageInfo.Length;

            var assemblies = await resolver.LoadNuGetAssemblies( token, allPackageInfo );
            if ( ( assemblies?.Length ?? 0 ) == packagesLength )
            {
               var dic = new Dictionary<(String PackageID, String AssemblyPath), Assembly>();
               for ( var i = 0; i < packagesLength; ++i )
               {
                  var key = (allPackageInfo[i].PackageID, allPackageInfo[i].AssemblyPath);
                  if ( !dic.ContainsKey( key ) )
                  {
                     dic.Add( key, assemblies[i] );
                  }
               }
               retVal = (T) ( await new DynamicConfigurableTypeLoader(
                  ( typeConfig, targetType ) =>
                  {
                     typeConfig = typeConfig.GetSection( typeConfigName );
                     TypeInfo loadedType = null;
                     if ( dic.TryGetValue( (GetRequiredStringValue( typeConfig, PACKAGE_ID ), ( GetOptionalStringValue( typeConfig, ASSEMBLY_PATH ) )), out var assembly )
                        && assembly != null
                        )
                     {
                        // Search for type within the assembly
                        var typeName = GetOptionalStringValue( typeConfig, TYPE_NAME );


                        var checkParentType = !String.IsNullOrEmpty( typeName );

                        if ( checkParentType )
                        {
                           // Instantiate directly
                           loadedType = assembly.DefinedTypes.FirstOrDefault( t => String.Equals( t.FullName, typeName ) );
                        }
                        else
                        {
                           // Search for first available
                           loadedType = assembly.DefinedTypes.FirstOrDefault( t => !t.IsAbstract && targetType.IsAssignableFrom( t ) );
                        }

                        if ( loadedType?.IsGenericTypeDefinition ?? false )
                        {
                           throw new NotImplementedException( "TODO: implement generic parameter substitution to type loader." );
                        }
                     }
                     return new ValueTask<TypeInfo>( loadedType );
                  } ).InstantiateWithConfiguration( configuration, typeof( T ).GetTypeInfo(), typeInfo =>
                      {
                         return DynamicConfigurableTypeLoader.DefaultConfigurationTypeLoaderCallback( typeInfo ) ??
                            typeInfo // If no [ConfigurationType] is provided, then try to bind to public constructor's single parameter type, if suitable constructor is found
                               .DeclaredConstructors
                               .Where( ctor => ctor.IsPublic && ctor.GetParameters().Length == 1 )
                               .FirstOrDefault()
                               ?.GetParameters()[0].ParameterType;
                      } ) );
            }

         }

         return retVal;
      }

      public static IEnumerable<(String PackageID, String PackageVersion, String AssemblyPath, String TypeName)> GetAllNuGetPackages(
         this IConfiguration configuration
         )
      {
         return configuration
            .AsDepthFirstEnumerable( c => c.GetChildren() )
            .Select( c => c.TryGetNuGetPackageConfiguration() )
            .Where( t => t.HasValue )
            .Select( t => t.Value );
      }

      public static (String PackageID, String PackageVersion, String AssemblyPath, String TypeName)? TryGetNuGetPackageConfiguration(
         this IConfiguration configuration
         )
      {
         // Basically, we need to scan for all sections containing:
         // "PackageID": mandatory, string
         // "PackageVersion": optional, string
         // "AssemblyPath": optional, string
         // "TypeName": mandatory, string
         String pID;
         if (
            configuration.GetChildren().Any()
            && !String.IsNullOrEmpty( pID = GetRequiredStringValue( configuration, PACKAGE_ID ) )
            )
         {
            return (pID, GetOptionalStringValue( configuration, PACKAGE_VERSION ), GetOptionalStringValue( configuration, ASSEMBLY_PATH ), GetOptionalStringValue( configuration, TYPE_NAME ));
         }
         else
         {
            return null;
         }
      }

      internal static String GetRequiredStringValue( IConfiguration config, String name )
      {
         var subSection = config.GetSection( name );
         return subSection.GetChildren().Any() ? // No children - this is terminating node
            null :
            subSection.Value; // We have some actual value
      }

      internal static String GetOptionalStringValue( IConfiguration config, String name )
      {
         var subSection = config.GetSection( name );
         return subSection.GetChildren().Any() ? // No children - this is terminating node
            null :
            subSection.Value;
      }
   }
}
