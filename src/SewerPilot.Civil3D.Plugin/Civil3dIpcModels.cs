using System;
using System.Collections.Generic;

namespace SewerPilot.Civil3D.Plugin
{
    public class IpcRequest
    {
        public string Command { get; set; }
        public string TargetType { get; set; }
        public string ElementName { get; set; }
        public string PropertyName { get; set; }
        public double? NewValueDouble { get; set; }
        public string NewValueString { get; set; }
    }

    public class IpcResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int Pid { get; set; }
        public string Civil3DVersion { get; set; }
        public string ActiveDocument { get; set; }
        public Civil3dNetworkDto NetworkData { get; set; }
        public Civil3dMutationResultDto MutationResult { get; set; }
        public Civil3dReadElementResultDto ReadElementResult { get; set; }
    }

    public class Civil3dNetworkDto
    {
        public string NetworkName { get; set; }
        public List<Civil3dPipeDto> Pipes { get; set; } = new List<Civil3dPipeDto>();
        public List<Civil3dStructureDto> Structures { get; set; } = new List<Civil3dStructureDto>();
    }

    public class Civil3dPipeDto
    {
        public string Handle { get; set; }
        public long ObjectIdVal { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string StartStructureName { get; set; }
        public string StartStructureHandle { get; set; }
        public string EndStructureName { get; set; }
        public string EndStructureHandle { get; set; }
        public double InnerDiameterOrWidth { get; set; }
        public double Length2D { get; set; }
        public double Slope { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double StartZ { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double EndZ { get; set; }
        public string FlowDirection { get; set; }
    }

    public class Civil3dStructureDto
    {
        public string Handle { get; set; }
        public long ObjectIdVal { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RimElevation { get; set; }
        public double SumpElevation { get; set; }
        public double SumpDepth { get; set; }
    }

    public class Civil3dMutationResultDto
    {
        public string TargetType { get; set; }
        public string ElementName { get; set; }
        public string PropertyName { get; set; }
        public double OldValueDouble { get; set; }
        public double NewValueDouble { get; set; }
        public string OldValueString { get; set; }
        public string NewValueString { get; set; }
        public bool TransactionCommitted { get; set; }
    }

    public class Civil3dReadElementResultDto
    {
        public string TargetType { get; set; }
        public string ElementName { get; set; }
        public string PropertyName { get; set; }
        public double ValueDouble { get; set; }
        public string ValueString { get; set; }
    }
}
