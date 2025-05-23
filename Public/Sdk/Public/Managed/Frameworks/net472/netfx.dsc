// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed  from 'Sdk.Managed';

const pkgContents = importFrom('Microsoft.NETFramework.ReferenceAssemblies.net472').Contents.all;

const relativeRoot = r`build/.NETFramework/v4.7.2`;

function createAssembly(binary: RelativePath) : Managed.Assembly {
    return Managed.Factory.createAssembly(pkgContents, r`${relativeRoot}/${binary}`, "net472", [], true);
}

function createFacade(binary: RelativePath) : Managed.Assembly {
    return Managed.Factory.createAssembly(pkgContents, r`${relativeRoot}/Facades/${binary}`, "net472", [], true);
}

export interface NetFxAssemblies {
    Accessibility: { dll: Managed.Assembly };
    CustomMarshalers: { dll: Managed.Assembly };
    ISymWrapper: { dll: Managed.Assembly };
    Netstandard: { dll: Managed.Assembly };
    Microsoft: {
        Activities: {
            Build: { dll: Managed.Assembly };
        },
        Build: {
            Conversion: {
                V40: { dll: Managed.Assembly };
            },
            dll:  Managed.Assembly;
            Engine: { dll: Managed.Assembly };
            Framework: { dll: Managed.Assembly };
            Tasks: {
                V40: { dll: Managed.Assembly };
            },
            Utilities: {
                V40: { dll: Managed.Assembly };
            },
        },
        CSharp: { dll: Managed.Assembly };
        JScript: { dll: Managed.Assembly };
        VisualBasic: {
            Compatibility: {
                Data: { dll: Managed.Assembly };
                dll:  Managed.Assembly;
            },
            dll:  Managed.Assembly;
        },
        VisualC: {
            dll:  Managed.Assembly;
            STLCLR: { dll: Managed.Assembly };
        },
        Win32: {
            Primitives: { dll: Managed.Assembly }
        }
    },
    MsCorLib: { dll: Managed.Assembly };
    PresentationBuildTasks: { dll: Managed.Assembly };
    PresentationCore: { dll: Managed.Assembly };
    PresentationFramework: {
        Aero: { dll: Managed.Assembly };
        Aero2: { dll: Managed.Assembly };
        AeroLite: { dll: Managed.Assembly };
        Classic: { dll: Managed.Assembly };
        dll:  Managed.Assembly;
        Luna: { dll: Managed.Assembly };
        Royale: { dll: Managed.Assembly };
    },
    ReachFramework: { dll: Managed.Assembly };
    sysglobl: { dll: Managed.Assembly };
    System: {
        Activities: {
            Core: {
                Presentation: { dll: Managed.Assembly };
            },
            dll:  Managed.Assembly;
            DurableInstancing: { dll: Managed.Assembly };
            Presentation: { dll: Managed.Assembly };
        },
        AddIn: {
            Contract: { dll: Managed.Assembly };
            dll:  Managed.Assembly;
        },
        AppContext: { dll: Managed.Assembly };
        Collections: {
            Concurrent: { dll: Managed.Assembly };
            NonGeneric: { dll: Managed.Assembly };
            Specialized: { dll: Managed.Assembly };
            dll:  Managed.Assembly;
        },
        Console: { dll: Managed.Assembly };
        ComponentModel: {
            Annotations: { dll: Managed.Assembly };
            Composition: {
                dll:  Managed.Assembly;
                Registration: { dll: Managed.Assembly };
            },
            DataAnnotations: { dll: Managed.Assembly };
            dll:  Managed.Assembly;
            EventBasedAsync: { dll: Managed.Assembly };
            Primitives: { dll: Managed.Assembly };
            TypeConverter: { dll: Managed.Assembly }
        },
        Configuration: {
            dll:  Managed.Assembly;
            Install: { dll: Managed.Assembly };
        },
        Core: { dll: Managed.Assembly };
        Data: {
            Common: { dll: Managed.Assembly };
            DataSetExtensions: { dll: Managed.Assembly };
            dll:  Managed.Assembly;
            Entity: {
                Design: { dll: Managed.Assembly };
                dll:  Managed.Assembly;
            },
            Linq: { dll: Managed.Assembly };
            OracleClient: { dll: Managed.Assembly };
            Services: {
                Client: { dll: Managed.Assembly };
                Design: { dll: Managed.Assembly };
                dll:  Managed.Assembly;
            },
            SqlXml: { dll: Managed.Assembly };
        },
        Deployment: { dll: Managed.Assembly };
        Design: { dll: Managed.Assembly };
        Device: { dll: Managed.Assembly };
        Diagnostics: {
            Contracts: { dll: Managed.Assembly };
            Debug: { dll: Managed.Assembly };
            FileVersionInfo: { dll: Managed.Assembly };
            Process: { dll: Managed.Assembly };
            StackTrace: { dll: Managed.Assembly };
            TextWriterTraceListener: { dll: Managed.Assembly };
            TraceSource: { dll: Managed.Assembly };
            Tools: { dll: Managed.Assembly };
            Tracing: { dll: Managed.Assembly };
        },
        DirectoryServices: {
            AccountManagement: { dll: Managed.Assembly };
            dll:  Managed.Assembly;
            Protocols: { dll: Managed.Assembly };
        },
        dll:  Managed.Assembly;
        Drawing: {
            Design: { dll: Managed.Assembly };
            Primitives: { dll: Managed.Assembly };
            dll:  Managed.Assembly;
        },
        Dynamic: {
            dll:  Managed.Assembly;
            Runtime: { dll: Managed.Assembly };
        },
        EnterpriseServices: {
            dll:  Managed.Assembly;
            Thunk: { dll: Managed.Assembly };
            Wrapper: { dll: Managed.Assembly };
        },
        Globalization: {
            dll:  Managed.Assembly;
            Calendars: { dll: Managed.Assembly };
            Extensions:{ dll: Managed.Assembly }
        },
        IdentityModel: {
            dll:  Managed.Assembly;
            Selectors: { dll: Managed.Assembly };
            Services: { dll: Managed.Assembly };
        },
        IO: {
            dll:  Managed.Assembly;
            Compression: {
                dll:  Managed.Assembly;
                FileSystem: { dll: Managed.Assembly };
                ZipFile: { dll: Managed.Assembly }
            },
            FileSystem: {
                dll:  Managed.Assembly;
                DriveInfo: { dll: Managed.Assembly };
                Primitives: { dll: Managed.Assembly };
                Watcher: { dll: Managed.Assembly };

            },
            Log: { dll: Managed.Assembly };
            IsolatedStorage: { dll: Managed.Assembly };
            MemoryMappedFiles: { dll: Managed.Assembly };
            Pipes: { dll: Managed.Assembly };
            UnmanagedMemoryStream: { dll: Managed.Assembly };
        },
        Linq: {
            dll:  Managed.Assembly;
            Expressions: { dll: Managed.Assembly };
            Parallel: { dll: Managed.Assembly };
            Queryable: { dll: Managed.Assembly };
        },
        Management: {
            dll:  Managed.Assembly;
            Instrumentation: { dll: Managed.Assembly };
        },
        Messaging: { dll: Managed.Assembly };
        Net: {
            dll:  Managed.Assembly;
            Http: {
                dll:  Managed.Assembly;
                Rtc: { dll: Managed.Assembly };
                WebRequest: { dll: Managed.Assembly };
            },
            NameResolution: { dll: Managed.Assembly };
            NetworkInformation: { dll: Managed.Assembly };
            Ping: { dll: Managed.Assembly };
            Primitives: { dll: Managed.Assembly };
            Requests: { dll: Managed.Assembly };
            Security: { dll: Managed.Assembly };
            Sockets: { dll: Managed.Assembly };
            WebHeaderCollection: { dll: Managed.Assembly };
            WebSockets: {
                dll:  Managed.Assembly;
                Client: { dll: Managed.Assembly };
            },
        },
        Numerics: { dll: Managed.Assembly };
        ObjectModel: { dll: Managed.Assembly };
        Printing: { dll: Managed.Assembly };
        Reflection: {
            Context: { dll: Managed.Assembly };
            dll:  Managed.Assembly;
            Emit: {
                dll:  Managed.Assembly;
                ILGeneration: { dll: Managed.Assembly };
                Lightweight: { dll: Managed.Assembly };
            },
            Extensions: { dll: Managed.Assembly };
            Primitives: { dll: Managed.Assembly };
        },
        Resources: {
            Reader: { dll: Managed.Assembly };
            ResourceManager: { dll: Managed.Assembly };
            Writer: { dll: Managed.Assembly };
        },
        Runtime: {
            dll:  Managed.Assembly;
            Caching: { dll: Managed.Assembly };
            CompilerServices: {
                VisualC: { dll: Managed.Assembly }
            },
            DurableInstancing: { dll: Managed.Assembly };
            Extensions: { dll: Managed.Assembly };
            Handles: { dll: Managed.Assembly };
            InteropServices: {
                dll:  Managed.Assembly;
                RuntimeInformation: { dll: Managed.Assembly };
                WindowsRuntime: { dll: Managed.Assembly };
            },
            Numerics: { dll: Managed.Assembly };
            Remoting: { dll: Managed.Assembly };
            Serialization: {
                dll:  Managed.Assembly;
                Formatters: {
                    dll:  Managed.Assembly;
                    Soap: { dll: Managed.Assembly };
                },
                Json: { dll: Managed.Assembly };
                Primitives: { dll: Managed.Assembly };
                Xml: { dll: Managed.Assembly };
            },
        },
        Security: {
            dll:  Managed.Assembly;
            Claims: { dll: Managed.Assembly };
            Cryptography: {
                Algorithms: { dll: Managed.Assembly };
                Csp: { dll: Managed.Assembly };
                Encoding: { dll: Managed.Assembly };
                Primitives: { dll: Managed.Assembly };
                X509Certificates: { dll: Managed.Assembly };
            },
            Principal: { dll: Managed.Assembly };
            SecureString: { dll: Managed.Assembly };
        },
        ServiceModel: {
            Activation: { dll: Managed.Assembly };
            Activities: { dll: Managed.Assembly };
            Channels: { dll: Managed.Assembly };
            Discovery: { dll: Managed.Assembly };
            dll:  Managed.Assembly;
            Duplex: { dll: Managed.Assembly };
            Http: { dll: Managed.Assembly };
            NetTcp: { dll: Managed.Assembly };
            Primitives: { dll: Managed.Assembly };
            Routing: { dll: Managed.Assembly };
            Security: { dll: Managed.Assembly };
            Web: { dll: Managed.Assembly };
        },
        ServiceProcess: { dll: Managed.Assembly };
        Speech: { dll: Managed.Assembly };
        Text: {
            Encoding: {
                dll:  Managed.Assembly;
                Extensions: { dll: Managed.Assembly };
            },
            RegularExpressions: { dll: Managed.Assembly };
        },
        Threading: {
            dll:  Managed.Assembly;
            Tasks: {
                dll:  Managed.Assembly;
                Parallel: { dll: Managed.Assembly };
            },
            Overlapped: { dll: Managed.Assembly };
            Thread: { dll: Managed.Assembly };
            ThreadPool: { dll: Managed.Assembly };
            Timer: { dll: Managed.Assembly };
        },
        Transactions: { dll: Managed.Assembly };
        ValueTuple: { dll: Managed.Assembly };
        Web: {
            Abstractions: { dll: Managed.Assembly };
            ApplicationServices: { dll: Managed.Assembly };
            DataVisualization: {
                Design: { dll: Managed.Assembly };
                dll:  Managed.Assembly;
            },
            dll:  Managed.Assembly;
            DynamicData: {
                Design: { dll: Managed.Assembly };
                dll:  Managed.Assembly;
            },
            Entity: {
                Design: { dll: Managed.Assembly };
                dll:  Managed.Assembly;
            },
            Extensions: {
                Design: { dll: Managed.Assembly };
                dll:  Managed.Assembly;
            },
            Mobile: { dll: Managed.Assembly };
            RegularExpressions: { dll: Managed.Assembly };
            Routing: { dll: Managed.Assembly };
            Services: { dll: Managed.Assembly };
        },
        Windows: {
            Controls: {
                Ribbon: { dll: Managed.Assembly };
            },
            dll:  Managed.Assembly;
            Forms: {
                DataVisualization: {
                    Design: { dll: Managed.Assembly };
                    dll:  Managed.Assembly;
                },
                dll:  Managed.Assembly;
            },
            Input: {
                Manipulations: { dll: Managed.Assembly };
            },
            Presentation: { dll: Managed.Assembly };
        },
        Workflow: {
            Activities: { dll: Managed.Assembly };
            ComponentModel: { dll: Managed.Assembly };
            Runtime: { dll: Managed.Assembly };
        },
        WorkflowServices: { dll: Managed.Assembly };
        Xaml: { dll: Managed.Assembly };
        Xml: {
            dll:  Managed.Assembly;
            Linq: { dll: Managed.Assembly };
            ReaderWriter: { dll: Managed.Assembly };
            Serialization: { dll: Managed.Assembly };
            XDocument: { dll: Managed.Assembly };
            XmlDocument: { dll: Managed.Assembly };
            XmlSerializer: { dll: Managed.Assembly };
            XPath: {
                dll:  Managed.Assembly;
                XDocument: { dll: Managed.Assembly };
            },
        },
    },
    UIAutomationClient: { dll: Managed.Assembly };
    UIAutomationClientsideProviders: { dll: Managed.Assembly };
    UIAutomationProvider: { dll: Managed.Assembly };
    UIAutomationTypes: { dll: Managed.Assembly };
    WindowsBase: { dll: Managed.Assembly };
    WindowsFormsIntegration: { dll: Managed.Assembly };
    XamlBuildTask: { dll: Managed.Assembly };
};

@@public
export const NetFx : NetFxAssemblies = {
    Accessibility: {
        dll: createAssembly(r`Accessibility.dll`),
    },
    CustomMarshalers: {
        dll: createAssembly(r`CustomMarshalers.dll`),
    },
    ISymWrapper: {
        dll: createAssembly(r`ISymWrapper.dll`),
    },
    Netstandard: {
        dll: createFacade(r`netstandard.dll`),
    },
    Microsoft: {
        Activities: {
            Build: {
                dll: createAssembly(r`Microsoft.Activities.Build.dll`),
            },
        },
        Build: {
            Conversion: {
                V40: {
                    dll: createAssembly(r`Microsoft.Build.Conversion.v4.0.dll`),
                },
            },
            dll: createAssembly(r`Microsoft.Build.dll`),
            Engine: {
                dll: createAssembly(r`Microsoft.Build.Engine.dll`),
            },
            Framework: {
                dll: createAssembly(r`Microsoft.Build.Framework.dll`),
            },
            Tasks: {
                V40: {
                    dll: createAssembly(r`Microsoft.Build.Tasks.v4.0.dll`),
                },
            },
            Utilities: {
                V40: {
                    dll: createAssembly(r`Microsoft.Build.Utilities.v4.0.dll`),
                },
            },
        },
        CSharp: {
            dll: createAssembly(r`Microsoft.CSharp.dll`),
        },
        JScript: {
            dll: createAssembly(r`Microsoft.JScript.dll`),
        },
        VisualBasic: {
            Compatibility: {
                Data: {
                    dll: createAssembly(r`Microsoft.VisualBasic.Compatibility.Data.dll`),
                },
                dll: createAssembly(r`Microsoft.VisualBasic.Compatibility.dll`),
            },
            dll: createAssembly(r`Microsoft.VisualBasic.dll`),
        },
        VisualC: {
            dll: createAssembly(r`Microsoft.VisualC.dll`),
            STLCLR: {
                dll: createAssembly(r`Microsoft.VisualC.STLCLR.dll`),
            },
        },
        Win32: {
            Primitives: {
                dll: createFacade(r`Microsoft.Win32.Primitives.dll`),
            }
        }
    },
    MsCorLib: {
        dll: createAssembly(r`mscorlib.dll`),
    },
    PresentationBuildTasks: {
        dll: createAssembly(r`PresentationBuildTasks.dll`),
    },
    PresentationCore: {
        dll: createAssembly(r`PresentationCore.dll`),
    },
    PresentationFramework: {
        Aero: {
            dll: createAssembly(r`PresentationFramework.Aero.dll`),
        },
        Aero2: {
            dll: createAssembly(r`PresentationFramework.Aero2.dll`),
        },
        AeroLite: {
            dll: createAssembly(r`PresentationFramework.AeroLite.dll`),
        },
        Classic: {
            dll: createAssembly(r`PresentationFramework.Classic.dll`),
        },
        dll: createAssembly(r`PresentationFramework.dll`),
        Luna: {
            dll: createAssembly(r`PresentationFramework.Luna.dll`),
        },
        Royale: {
            dll: createAssembly(r`PresentationFramework.Royale.dll`),
        },
    },
    ReachFramework: {
        dll: createAssembly(r`ReachFramework.dll`),
    },
    sysglobl: {
        dll: createAssembly(r`sysglobl.dll`),
    },
    System: {
        Activities: {
            Core: {
                Presentation: {
                    dll: createAssembly(r`System.Activities.Core.Presentation.dll`),
                },
            },
            dll: createAssembly(r`System.Activities.dll`),
            DurableInstancing: {
                dll: createAssembly(r`System.Activities.DurableInstancing.dll`),
            },
            Presentation: {
                dll: createAssembly(r`System.Activities.Presentation.dll`),
            },
        },
        AddIn: {
            Contract: {
                dll: createAssembly(r`System.AddIn.Contract.dll`),
            },
            dll: createAssembly(r`System.AddIn.dll`),
        },
        AppContext: {
            dll: createFacade(r`System.AppContext.dll`),
        },
        Collections: {
            Concurrent: {
                dll: createFacade(r`System.Collections.Concurrent.dll`),
            },
            NonGeneric: {
                dll: createFacade(r`System.Collections.NonGeneric.dll`),
            },
            Specialized: {
                dll: createFacade(r`System.Collections.Specialized.dll`),
            },
            dll: createFacade(r`System.Collections.dll`),
        },
        Console: {
            dll: createFacade(r`System.Console.dll`),
        },
        ComponentModel: {
            Annotations: {
                dll: createFacade(r`System.ComponentModel.Annotations.dll`),
            },
            Composition: {
                dll: createAssembly(r`System.ComponentModel.Composition.dll`),
                Registration: {
                    dll: createAssembly(r`System.ComponentModel.Composition.Registration.dll`),
                },
            },
            DataAnnotations: {
                dll: createAssembly(r`System.ComponentModel.DataAnnotations.dll`),
            },
            dll: createFacade(r`System.ComponentModel.dll`),
            EventBasedAsync: {
                dll: createFacade(r`System.ComponentModel.EventBasedAsync.dll`),
            },
            Primitives: {
                dll: createFacade(r`System.ComponentModel.Primitives.dll`),
            },
            TypeConverter: {
                dll: createFacade(r`System.ComponentModel.TypeConverter.dll`),
            }
        },
        Configuration: {
            dll: createAssembly(r`System.Configuration.dll`),
            Install: {
                dll: createAssembly(r`System.Configuration.Install.dll`),
            },
        },
        Core: {
            dll: createAssembly(r`System.Core.dll`),
        },
        Data: {
            Common: {
                dll: createFacade(r`System.Data.Common.dll`),
            },
            DataSetExtensions: {
                dll: createAssembly(r`System.Data.DataSetExtensions.dll`),
            },
            dll: createAssembly(r`System.Data.dll`),
            Entity: {
                Design: {
                    dll: createAssembly(r`System.Data.Entity.Design.dll`),
                },
                dll: createAssembly(r`System.Data.Entity.dll`),
            },
            Linq: {
                dll: createAssembly(r`System.Data.Linq.dll`),
            },
            OracleClient: {
                dll: createAssembly(r`System.Data.OracleClient.dll`),
            },
            Services: {
                Client: {
                    dll: createAssembly(r`System.Data.Services.Client.dll`),
                },
                Design: {
                    dll: createAssembly(r`System.Data.Services.Design.dll`),
                },
                dll: createAssembly(r`System.Data.Services.dll`),
            },
            SqlXml: {
                dll: createAssembly(r`System.Data.SqlXml.dll`),
            },
        },
        Deployment: {
            dll: createAssembly(r`System.Deployment.dll`),
        },
        Design: {
            dll: createAssembly(r`System.Design.dll`),
        },
        Device: {
            dll: createAssembly(r`System.Device.dll`),
        },
        Diagnostics: {
            Contracts: {
                dll: createFacade(r`System.Diagnostics.Contracts.dll`),
            },
            Debug: {
                dll: createFacade(r`System.Diagnostics.Debug.dll`),
            },
            FileVersionInfo: {
                dll: createFacade(r`System.Diagnostics.FileVersionInfo.dll`),
            },
            Process: {
                dll: createFacade(r`System.Diagnostics.Process.dll`),
            },
            StackTrace: {
                dll: createFacade(r`System.Diagnostics.StackTrace.dll`),
            },
            TextWriterTraceListener: {
                dll: createFacade(r`System.Diagnostics.TextWriterTraceListener.dll`),
            },
            TraceSource: {
                dll: createFacade(r`System.Diagnostics.TraceSource.dll`),
            },
            Tools: {
                dll: createFacade(r`System.Diagnostics.Tools.dll`),
            },
            Tracing: {
                dll: createAssembly(r`System.Diagnostics.Tracing.dll`),
            },
        },
        DirectoryServices: {
            AccountManagement: {
                dll: createAssembly(r`System.DirectoryServices.AccountManagement.dll`),
            },
            dll: createAssembly(r`System.DirectoryServices.dll`),
            Protocols: {
                dll: createAssembly(r`System.DirectoryServices.Protocols.dll`),
            },
        },
        dll: createAssembly(r`System.dll`),
        Drawing: {
            Design: {
                dll: createAssembly(r`System.Drawing.Design.dll`),
            },
            Primitives: {
                dll: createFacade(r`System.Drawing.Primitives.dll`),
            },
            dll: createAssembly(r`System.Drawing.dll`),
        },
        Dynamic: {
            dll: createAssembly(r`System.Dynamic.dll`),
            Runtime: {
                dll: createFacade(r`System.Dynamic.Runtime.dll`),
            },
        },
        EnterpriseServices: {
            dll: createAssembly(r`System.EnterpriseServices.dll`),
            Thunk: {
                dll: createAssembly(r`System.EnterpriseServices.Thunk.dll`),
            },
            Wrapper: {
                dll: createAssembly(r`System.EnterpriseServices.Wrapper.dll`),
            },
        },
        Globalization: {
            dll: createFacade(r`System.Globalization.dll`),
            Calendars: {
                dll: createFacade(r`System.Globalization.Calendars.dll`),
            },
            Extensions:{
                dll: createFacade(r`System.Globalization.Extensions.dll`),
            }
        },
        IdentityModel: {
            dll: createAssembly(r`System.IdentityModel.dll`),
            Selectors: {
                dll: createAssembly(r`System.IdentityModel.Selectors.dll`),
            },
            Services: {
                dll: createAssembly(r`System.IdentityModel.Services.dll`),
            },
        },
        IO: {
            dll: createFacade(r`System.IO.dll`),
            Compression: {
                dll: createAssembly(r`System.IO.Compression.dll`),
                FileSystem: {
                    dll: createAssembly(r`System.IO.Compression.FileSystem.dll`),
                },
                ZipFile: {
                    dll: createFacade(r`System.IO.Compression.ZipFile.dll`),
                }
            },
            FileSystem: {
                dll: createFacade(r`System.IO.FileSystem.dll`),
                DriveInfo: {
                    dll: createFacade(r`System.IO.FileSystem.DriveInfo.dll`),
                },
                Primitives: {
                    dll: createFacade(r`System.IO.FileSystem.Primitives.dll`),
                },
                Watcher: {
                    dll: createFacade(r`System.IO.FileSystem.Watcher.dll`),
                },

            },
            Log: {
                dll: createAssembly(r`System.IO.Log.dll`),
            },
            IsolatedStorage: {
                dll: createFacade(r`System.IO.IsolatedStorage.dll`),
            },
            MemoryMappedFiles: {
                dll: createFacade(r`System.IO.MemoryMappedFiles.dll`),
            },
            Pipes: {
                dll: createFacade(r`System.IO.Pipes.dll`),
            },
            UnmanagedMemoryStream: {
                dll: createFacade(r`System.IO.UnmanagedMemoryStream.dll`),
            },
        },
        Linq: {
            dll: createFacade(r`System.Linq.dll`),
            Expressions: {
                dll: createFacade(r`System.Linq.Expressions.dll`),
            },
            Parallel: {
                dll: createFacade(r`System.Linq.Parallel.dll`),
            },
            Queryable: {
                dll: createFacade(r`System.Linq.Queryable.dll`),
            },
        },
        Management: {
            dll: createAssembly(r`System.Management.dll`),
            Instrumentation: {
                dll: createAssembly(r`System.Management.Instrumentation.dll`),
            },
        },
        Messaging: {
            dll: createAssembly(r`System.Messaging.dll`),
        },
        Net: {
            dll: createAssembly(r`System.Net.dll`),
            Http: {
                dll: createAssembly(r`System.Net.Http.dll`),
                Rtc: {
                    dll: createFacade(r`System.Net.Http.Rtc.dll`),
                },
                WebRequest: {
                    dll: createAssembly(r`System.Net.Http.WebRequest.dll`),
                },
            },
            NameResolution: {
                dll: createFacade(r`System.Net.NameResolution.dll`),
            },
            NetworkInformation: {
                dll: createFacade(r`System.Net.NetworkInformation.dll`),
            },
            Ping: {
                dll: createFacade(r`System.Net.Ping.dll`),
            },
            Primitives: {
                dll: createFacade(r`System.Net.Primitives.dll`),
            },
            Requests: {
                dll: createFacade(r`System.Net.Requests.dll`),
            },
            Security: {
                dll: createFacade(r`System.Net.Security.dll`),
            },
            Sockets: {
                dll: createFacade(r`System.Net.Sockets.dll`),
            },
            WebHeaderCollection: {
                dll: createFacade(r`System.Net.WebHeaderCollection.dll`),
            },
            WebSockets: {
                dll: createFacade(r`System.Net.WebSockets.dll`),
                Client: {
                    dll: createFacade(r`System.Net.WebSockets.Client.dll`),
                },
            },
        },
        Numerics: {
            dll: createAssembly(r`System.Numerics.dll`),
        },
        ObjectModel: {
            dll: createFacade(r`System.ObjectModel.dll`),
        },
        Printing: {
            dll: createAssembly(r`System.Printing.dll`),
        },
        Reflection: {
            Context: {
                dll: createAssembly(r`System.Reflection.Context.dll`),
            },
            dll: createFacade(r`System.Reflection.dll`),
            Emit: {
                dll: createFacade(r`System.Reflection.Emit.dll`),
                ILGeneration: {
                    dll: createFacade(r`System.Reflection.Emit.ILGeneration.dll`),
                },
                Lightweight: {
                    dll: createFacade(r`System.Reflection.Emit.Lightweight.dll`),
                },
            },
            Extensions: {
                dll: createFacade(r`System.Reflection.Extensions.dll`),
            },
            Primitives: {
                dll: createFacade(r`System.Reflection.Primitives.dll`),
            },
        },
        Resources: {
            Reader: {
                dll: createFacade(r`System.Resources.Reader.dll`),
            },
            ResourceManager: {
                dll: createFacade(r`System.Resources.ResourceManager.dll`),
            },
            Writer: {
                dll: createFacade(r`System.Resources.Writer.dll`),
            },
        },
        Runtime: {
            dll: createFacade(r`System.Runtime.dll`),
            Caching: {
                dll: createAssembly(r`System.Runtime.Caching.dll`),
            },
            CompilerServices: {
                VisualC: {
                    dll: createFacade(r`System.Runtime.CompilerServices.VisualC.dll`),
                }
            },
            DurableInstancing: {
                dll: createAssembly(r`System.Runtime.DurableInstancing.dll`),
            },
            Extensions: {
                dll: createFacade(r`System.Runtime.Extensions.dll`),
            },
            Handles: {
                dll: createFacade(r`System.Runtime.Handles.dll`),
            },
            InteropServices: {
                dll: createFacade(r`System.Runtime.InteropServices.dll`),
                RuntimeInformation: {
                    dll: createFacade(r`System.Runtime.InteropServices.RuntimeInformation.dll`),
                },
                WindowsRuntime: {
                    dll: createFacade(r`System.Runtime.InteropServices.WindowsRuntime.dll`),
                },
            },
            Numerics: {
                dll: createFacade(r`System.Runtime.Numerics.dll`),
            },
            Remoting: {
                dll: createAssembly(r`System.Runtime.Remoting.dll`),
            },
            Serialization: {
                dll: createAssembly(r`System.Runtime.Serialization.dll`),
                Formatters: {
                    dll: createFacade(r`System.Runtime.Serialization.Formatters.dll`),
                    Soap: {
                        dll: createAssembly(r`System.Runtime.Serialization.Formatters.Soap.dll`),
                    },
                },
                Json: {
                    dll: createFacade(r`System.Runtime.Serialization.Json.dll`),
                },
                Primitives: {
                    dll: createFacade(r`System.Runtime.Serialization.Primitives.dll`),
                },
                Xml: {
                    dll: createFacade(r`System.Runtime.Serialization.Xml.dll`),
                },
            },
        },
        Security: {
            dll: createAssembly(r`System.Security.dll`),
            Claims: {
                dll: createFacade(r`System.Security.Claims.dll`),
            },
            Cryptography: {
                Algorithms: {
                    dll: createFacade(r`System.Security.Cryptography.Algorithms.dll`),
                },
                Csp: {
                    dll: createFacade(r`System.Security.Cryptography.Csp.dll`),
                },
                Encoding: {
                    dll: createFacade(r`System.Security.Cryptography.Encoding.dll`),
                },
                Primitives: {
                    dll: createFacade(r`System.Security.Cryptography.Primitives.dll`),
                },
                X509Certificates: {
                    dll: createFacade(r`System.Security.Cryptography.X509Certificates.dll`),
                },
            },
            Principal: {
                dll: createFacade(r`System.Security.Principal.dll`),
            },
            SecureString: {
                dll: createFacade(r`System.Security.SecureString.dll`),
            },
        },
        ServiceModel: {
            Activation: {
                dll: createAssembly(r`System.ServiceModel.Activation.dll`),
            },
            Activities: {
                dll: createAssembly(r`System.ServiceModel.Activities.dll`),
            },
            Channels: {
                dll: createAssembly(r`System.ServiceModel.Channels.dll`),
            },
            Discovery: {
                dll: createAssembly(r`System.ServiceModel.Discovery.dll`),
            },
            dll: createAssembly(r`System.ServiceModel.dll`),
            Duplex: {
                dll: createFacade(r`System.ServiceModel.Duplex.dll`),
            },
            Http: {
                dll: createFacade(r`System.ServiceModel.Http.dll`),
            },
            NetTcp: {
                dll: createFacade(r`System.ServiceModel.NetTcp.dll`),
            },
            Primitives: {
                dll: createFacade(r`System.ServiceModel.Primitives.dll`),
            },
            Routing: {
                dll: createAssembly(r`System.ServiceModel.Routing.dll`),
            },
            Security: {
                dll: createFacade(r`System.ServiceModel.Security.dll`),
            },
            Web: {
                dll: createAssembly(r`System.ServiceModel.Web.dll`),
            },
        },
        ServiceProcess: {
            dll: createAssembly(r`System.ServiceProcess.dll`),
        },
        Speech: {
            dll: createAssembly(r`System.Speech.dll`),
        },
        Text: {
            Encoding: {
                dll: createFacade(r`System.Text.Encoding.dll`),
                Extensions: {
                    dll: createFacade(r`System.Text.Encoding.Extensions.dll`),
                },
            },
            RegularExpressions: {
                dll: createFacade(r`System.Text.RegularExpressions.dll`),
            },
        },
        Threading: {
            dll: createFacade(r`System.Threading.dll`),
            Tasks: {
                dll: createFacade(r`System.Threading.Tasks.dll`),
                Parallel: {
                    dll: createFacade(r`System.Threading.Tasks.Parallel.dll`),
                },
            },
            Overlapped: {
                dll: createFacade(r`System.Threading.Overlapped.dll`),
            },
            Thread: {
                dll: createFacade(r`System.Threading.Thread.dll`),
            },
            ThreadPool: {
                dll: createFacade(r`System.Threading.ThreadPool.dll`),
            },
            Timer: {
                dll: createFacade(r`System.Threading.Timer.dll`),
            },
        },
        Transactions: {
            dll: createAssembly(r`System.Transactions.dll`),
        },
        ValueTuple: {
            dll: createFacade(r`System.ValueTuple.dll`),
        },
        Web: {
            Abstractions: {
                dll: createAssembly(r`System.Web.Abstractions.dll`),
            },
            ApplicationServices: {
                dll: createAssembly(r`System.Web.ApplicationServices.dll`),
            },
            DataVisualization: {
                Design: {
                    dll: createAssembly(r`System.Web.DataVisualization.Design.dll`),
                },
                dll: createAssembly(r`System.Web.DataVisualization.dll`),
            },
            dll: createAssembly(r`System.Web.dll`),
            DynamicData: {
                Design: {
                    dll: createAssembly(r`System.Web.DynamicData.Design.dll`),
                },
                dll: createAssembly(r`System.Web.DynamicData.dll`),
            },
            Entity: {
                Design: {
                    dll: createAssembly(r`System.Web.Entity.Design.dll`),
                },
                dll: createAssembly(r`System.Web.Entity.dll`),
            },
            Extensions: {
                Design: {
                    dll: createAssembly(r`System.Web.Extensions.Design.dll`),
                },
                dll: createAssembly(r`System.Web.Extensions.dll`),
            },
            Mobile: {
                dll: createAssembly(r`System.Web.Mobile.dll`),
            },
            RegularExpressions: {
                dll: createAssembly(r`System.Web.RegularExpressions.dll`),
            },
            Routing: {
                dll: createAssembly(r`System.Web.Routing.dll`),
            },
            Services: {
                dll: createAssembly(r`System.Web.Services.dll`),
            },
        },
        Windows: {
            Controls: {
                Ribbon: {
                    dll: createAssembly(r`System.Windows.Controls.Ribbon.dll`),
                },
            },
            dll: createAssembly(r`System.Windows.dll`),
            Forms: {
                DataVisualization: {
                    Design: {
                        dll: createAssembly(r`System.Windows.Forms.DataVisualization.Design.dll`),
                    },
                    dll: createAssembly(r`System.Windows.Forms.DataVisualization.dll`),
                },
                dll: createAssembly(r`System.Windows.Forms.dll`),
            },
            Input: {
                Manipulations: {
                    dll: createAssembly(r`System.Windows.Input.Manipulations.dll`),
                },
            },
            Presentation: {
                dll: createAssembly(r`System.Windows.Presentation.dll`),
            },
        },
        Workflow: {
            Activities: {
                dll: createAssembly(r`System.Workflow.Activities.dll`),
            },
            ComponentModel: {
                dll: createAssembly(r`System.Workflow.ComponentModel.dll`),
            },
            Runtime: {
                dll: createAssembly(r`System.Workflow.Runtime.dll`),
            },
        },
        WorkflowServices: {
            dll: createAssembly(r`System.WorkflowServices.dll`),
        },
        Xaml: {
            dll: createAssembly(r`System.Xaml.dll`),
        },
        Xml: {
            dll: createAssembly(r`System.Xml.dll`),
            Linq: {
                dll: createAssembly(r`System.Xml.Linq.dll`),
            },
            ReaderWriter: {
                dll: createFacade(r`System.Xml.ReaderWriter.dll`),
            },
            Serialization: {
                dll: createAssembly(r`System.Xml.Serialization.dll`),
            },
            XDocument: {
                dll: createFacade(r`System.Xml.XDocument.dll`),
            },
            XmlDocument: {
                dll: createFacade(r`System.Xml.XmlDocument.dll`),
            },
            XmlSerializer: {
                dll: createFacade(r`System.Xml.XmlSerializer.dll`),
            },
            XPath: {
                dll: createFacade(r`System.Xml.XPath.dll`),
                XDocument: {
                    dll: createFacade(r`System.Xml.XPath.XDocument.dll`),
                },
            },
        },
    },
    UIAutomationClient: {
        dll: createAssembly(r`UIAutomationClient.dll`),
    },
    UIAutomationClientsideProviders: {
        dll: createAssembly(r`UIAutomationClientsideProviders.dll`),
    },
    UIAutomationProvider: {
        dll: createAssembly(r`UIAutomationProvider.dll`),
    },
    UIAutomationTypes: {
        dll: createAssembly(r`UIAutomationTypes.dll`),
    },
    WindowsBase: {
        dll: createAssembly(r`WindowsBase.dll`),
    },
    WindowsFormsIntegration: {
        dll: createAssembly(r`WindowsFormsIntegration.dll`),
    },
    XamlBuildTask: {
        dll: createAssembly(r`XamlBuildTask.dll`),
    },
};
