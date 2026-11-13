using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    // Uses the public formatter called by Bentley's description-update action. No UI or text synthesis.
    internal static class P14NativeConduitLabelFormatter
    {
        internal sealed class Result
        {
            public string FormatterContract { get; set; }
            public string NativeTemplate { get; set; }
            public string GeneratedLabel { get; set; }
            public Dictionary<string,string> NativeDisplayUnits { get; set; }
        }
        private static Type Contract(string assembly, string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Single(a=>!a.IsDynamic && a.GetName().Name==assembly).GetType(name,true,false);
        private static object Value(object field, int id) => Contract("Haestad.Support","Haestad.Support.Support.IField")
            .GetMethod("GetValue",new[]{typeof(int)}).Invoke(field,new object[]{id});
        internal static object PhysicalField(object manager, string name)
        {
            Type contract=Contract("Haestad.Domain","Haestad.Domain.IDomainElementManager");
            object field=contract.GetMethod("DomainElementField",new[]{typeof(string),typeof(string)})
                .Invoke(manager,new object[]{name,"Physical"});
            if(field==null) throw new MissingMemberException("Native Physical field unavailable: "+name);
            return field;
        }
        internal static Result Generate(object dataset, object project, object manager,int id=150)
        {
            Type projectType=Contract("Haestad.Framework","Haestad.Framework.Application.IProject");
            object npm=projectType.GetProperty("NumericPresentationManager").GetValue(project,null);
            if(npm==null) throw new InvalidDataException("Native numeric presentation manager unavailable.");
            Type dataType=Contract("Haestad.Domain","Haestad.Domain.IDomainDataSet");
            object fm=dataType.GetProperty("FieldManager").GetValue(dataset,null);
            object templateField=Contract("Haestad.Domain","Haestad.Domain.IFieldManager")
                .GetMethod("DomainDataSetField",new[]{typeof(string)}).Invoke(fm,new object[]{"HmCircularPipeLabelFormat"});
            int datasetId=Convert.ToInt32(dataset.GetType().GetProperty("Id").GetValue(dataset,null),CultureInfo.InvariantCulture);
            string template=Convert.ToString(Value(templateField,datasetId),CultureInfo.InvariantCulture);
            if(string.IsNullOrWhiteSpace(template)) throw new InvalidDataException("Native circular-pipe description template is absent.");

            Type supportType=Contract("Haestad.Domain","Haestad.Domain.SupportElementType");
            int catalogType=Convert.ToInt32(WaterObjectsLifecycleHost.EnumMember(supportType,"CatalogConduitElementManager"),CultureInfo.InvariantCulture);
            object catalogs=dataType.GetMethod("SupportElementManager",new[]{typeof(int)}).Invoke(dataset,new object[]{catalogType});
            var fieldNames=new Dictionary<string,string>(StringComparer.Ordinal) {
                ["conduitBaseWidthField"]="TrapezoidalChannelType_BaseWidth",
                ["conduitCatalogIdField"]="CatalogPipeType_CatalogPipeID",
                ["conduitDiameterField"]="ConduitDiameter",["leftSideSlopeField"]="TrapezoidalChannelType_LeftSideSlope",
                ["rightSideSlopeField"]="TrapezoidalChannelType_RightSideSlope",["conduitShapeField"]="ConduitShape",
                ["conduitSpanField"]="ConduitSpan",["conduitRiseField"]="ConduitRise",
                ["bottomRadiusField"]="RectRoundChannelType_RectBottomRadius",
                ["triangleHeightField"]="RectTriangleChannelType_RectTriangleTriangleHeight",
                ["exponentField"]="PowerChannelType_PowerExponenet",["conduitTypeField"]="ConduitType",
                ["conduitUnitLengthField"]="ConduitUnitLength"
            };
            var unitNames=new Dictionary<string,string>(StringComparer.Ordinal) {
                ["diameterUnit"]="Diameter",["elevationUnit"]="Elevation",["sideSlopeUnit"]="SideSlope",
                ["lengthUnit"]="Length",["stationUnit"]="StationFormatter",["widthUnit"]="Width"
            };
            Type library=Contract("Haestad.Framework","Haestad.Framework.Support.NumericFormatterLibrary");
            Type formatterEnum=Contract("Haestad.Framework","Haestad.Framework.Support.StandardNumericFormatters");
            MethodInfo formatterName=library.GetMethod("GetNumericFormatterName",new[]{formatterEnum});
            var units=new Dictionary<string,string>(StringComparer.Ordinal);
            var values=new Dictionary<string,object>(StringComparer.Ordinal) {
                ["formatString"]=template,["elementId"]=id,["npm"]=npm,["conduitCatalogElementManager"]=catalogs
            };
            foreach(var item in fieldNames) values[item.Key]=PhysicalField(manager,item.Value);
            foreach(var item in unitNames)
            {
                object enumValue=Enum.Parse(formatterEnum,item.Value,false);
                string key=(string)formatterName.Invoke(null,new[]{enumValue});
                object numeric=npm.GetType().GetProperty("Item",new[]{typeof(string)}).GetValue(npm,new object[]{key});
                object unit=numeric.GetType().GetProperty("DisplayUnit").GetValue(numeric,null);
                if(unit==null) throw new InvalidDataException("Native display unit unavailable: "+item.Value);
                values[item.Key]=unit;units[item.Key]=Convert.ToString(unit,CultureInfo.InvariantCulture);
            }
            Type formatter=Contract("Haestad.Framework","Haestad.Framework.Support.StormSewerPipeInventoryCalculator");
            MethodInfo method=formatter.GetMethods(BindingFlags.Public|BindingFlags.Static)
                .Single(m=>m.Name=="FormatConduitShapeLabel" && m.GetParameters().Length==23);
            if(!formatter.IsPublic || method.ReturnType!=typeof(string)) throw new InvalidDataException("Native public formatter contract mismatch.");
            var parameters=method.GetParameters();
            if(parameters.Any(p=>!values.ContainsKey(p.Name))) throw new InvalidDataException("Native formatter parameter contract changed.");
            string generated=(string)RuntimeStageJournal.Call("P14NativeConduitDescriptionFormatter",()=>method.Invoke(null,parameters.Select(p=>values[p.Name]).ToArray()));
            if(string.IsNullOrWhiteSpace(generated)) throw new InvalidDataException("Native formatter returned an empty description.");
            return new Result {FormatterContract=formatter.FullName+"."+method,NativeTemplate=template,
                GeneratedLabel=generated,NativeDisplayUnits=units};
        }
    }
}
