using System;
using System.Diagnostics;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

[assembly: ExtensionApplication(typeof(SewerPilot.Civil3D.Plugin.SewerPilotCivil3dExtension))]

namespace SewerPilot.Civil3D.Plugin
{
    public class SewerPilotCivil3dExtension : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                var ed = doc?.Editor;
                ed?.WriteMessage(string.Format("\n[SewerPilot] Civil 3D In-Process Extension initialized. PID: {0}\n",
                    Process.GetCurrentProcess().Id));
            }
            catch (System.Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in SewerPilotCivil3dExtension.cs: " + suppressedEx.Message); }
        }

        public void Terminate()
        {
        }

        [CommandMethod("SEWERPILOT_IPC_START")]
        public void StartIpcServer()
        {
            Civil3dIpcServer.RunServer();
        }
    }
}
