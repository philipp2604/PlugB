using Google.Protobuf;
using PlugB.Models;
using ProtoDataSet = Com.Cirruslink.Sparkplug.Protobuf.Payload.Types.DataSet;

// Aliases for the generated Protobuf classes using the official Cirrus Link namespace
using ProtoMetric = Com.Cirruslink.Sparkplug.Protobuf.Payload.Types.Metric;
using ProtoPropertySet = Com.Cirruslink.Sparkplug.Protobuf.Payload.Types.PropertySet;
using ProtoPropertyValue = Com.Cirruslink.Sparkplug.Protobuf.Payload.Types.PropertyValue;
using ProtoTemplate = Com.Cirruslink.Sparkplug.Protobuf.Payload.Types.Template;

namespace PlugB.Internal.Mapping;

internal static class DataTypeConverter
{
    public static void ApplyToProtoMetric(ProtoMetric protoMetric, Metric metric)
    {
        protoMetric.Datatype = (uint)metric.DataType;

        if (metric.Value == null)
        {
            protoMetric.IsNull = true;
            return;
        }

        switch (metric.DataType)
        {
            case PlugBDataType.Int8:
            case PlugBDataType.Int16:
            case PlugBDataType.Int32:
                protoMetric.IntValue = Convert.ToUInt32(metric.Value);
                break;

            case PlugBDataType.Int64:
                protoMetric.LongValue = (ulong)Convert.ToInt64(metric.Value);
                break;

            // all unsigned types are stored in long_value!
            case PlugBDataType.UInt8:
            case PlugBDataType.UInt16:
            case PlugBDataType.UInt32:
            case PlugBDataType.UInt64:
                protoMetric.LongValue = Convert.ToUInt64(metric.Value);
                break;

            case PlugBDataType.Float:
                protoMetric.FloatValue = Convert.ToSingle(metric.Value);
                break;

            case PlugBDataType.Double:
                protoMetric.DoubleValue = Convert.ToDouble(metric.Value);
                break;

            case PlugBDataType.Boolean:
                protoMetric.BooleanValue = Convert.ToBoolean(metric.Value);
                break;

            case PlugBDataType.String:
            case PlugBDataType.Text:
            case PlugBDataType.Uuid:
                protoMetric.StringValue = metric.Value.ToString() ?? string.Empty;
                break;

            case PlugBDataType.DateTime:
                if (metric.Value is DateTime dt)
                    protoMetric.LongValue = (ulong)((DateTimeOffset)dt).ToUnixTimeMilliseconds();
                else if (metric.Value is DateTimeOffset dto)
                    protoMetric.LongValue = (ulong)dto.ToUnixTimeMilliseconds();
                else
                    protoMetric.LongValue = Convert.ToUInt64(metric.Value);
                break;

            case PlugBDataType.Bytes:
            case PlugBDataType.File:
                if (metric.Value is byte[] bytes)
                    protoMetric.BytesValue = ByteString.CopyFrom(bytes);
                else
                    throw new ArgumentException($"Value for {metric.DataType} must be of type byte[].");
                break;

            case PlugBDataType.DataSet:
                if (metric.Value is PlugBDataSet ds)
                    protoMetric.DatasetValue = ConvertDataSet(ds);
                else
                    throw new ArgumentException("Value must be of type PlugBDataSet.");
                break;

            case PlugBDataType.Template:
                if (metric.Value is PlugBTemplate tpl)
                    protoMetric.TemplateValue = ConvertTemplate(tpl);
                else
                    throw new ArgumentException("Value must be of type PlugBTemplate.");
                break;

            case PlugBDataType.PropertySet:
                if (metric.Value is PlugBPropertySet props)
                    protoMetric.Properties = ConvertPropertySet(props);
                else
                    throw new ArgumentException("Value must be of type PlugBPropertySet.");
                break;

            default:
                throw new NotSupportedException($"DataType {metric.DataType} is not mapped yet.");
        }
    }

    internal static ProtoPropertySet ConvertPropertySet(PlugBPropertySet propertySet)
    {
        var protoProps = new ProtoPropertySet();

        foreach (var kvp in propertySet.Properties)
        {
            protoProps.Keys.Add(kvp.Key);
            protoProps.Values.Add(ConvertPropertyValue(kvp.Value));
        }

        return protoProps;
    }

    private static ProtoPropertyValue ConvertPropertyValue(PlugBPropertyValue propValue)
    {
        var protoVal = new ProtoPropertyValue
        {
            Type = (uint)propValue.DataType
        };

        if (propValue.Value == null)
        {
            protoVal.IsNull = true;
            return protoVal;
        }

        switch (propValue.DataType)
        {
            case PlugBDataType.Int8:
            case PlugBDataType.Int16:
            case PlugBDataType.Int32:
                protoVal.IntValue = Convert.ToUInt32(propValue.Value);
                break;

            case PlugBDataType.Int64:
                protoVal.LongValue = (ulong)Convert.ToInt64(propValue.Value);
                break;

            case PlugBDataType.UInt8:
            case PlugBDataType.UInt16:
            case PlugBDataType.UInt32:
            case PlugBDataType.UInt64:
                protoVal.LongValue = Convert.ToUInt64(propValue.Value);
                break;

            case PlugBDataType.Float:
                protoVal.FloatValue = Convert.ToSingle(propValue.Value);
                break;

            case PlugBDataType.Double:
                protoVal.DoubleValue = Convert.ToDouble(propValue.Value);
                break;

            case PlugBDataType.Boolean:
                protoVal.BooleanValue = Convert.ToBoolean(propValue.Value);
                break;

            case PlugBDataType.String:
            case PlugBDataType.Text:
            case PlugBDataType.Uuid:
                protoVal.StringValue = propValue.Value.ToString() ?? string.Empty;
                break;

            case PlugBDataType.DateTime:
                if (propValue.Value is DateTime dt)
                    protoVal.LongValue = (ulong)((DateTimeOffset)dt).ToUnixTimeMilliseconds();
                else if (propValue.Value is DateTimeOffset dto)
                    protoVal.LongValue = (ulong)dto.ToUnixTimeMilliseconds();
                else
                    protoVal.LongValue = Convert.ToUInt64(propValue.Value);
                break;

            case PlugBDataType.PropertySet:
                if (propValue.Value is PlugBPropertySet innerProps)
                    protoVal.PropertysetValue = ConvertPropertySet(innerProps);
                else
                    throw new ArgumentException("PropertyValue must be of type PlugBPropertySet.");
                break;

            default:
                throw new NotSupportedException($"DataType {propValue.DataType} is not supported in PropertySet.");
        }

        return protoVal;
    }

    // ConvertDataSet and ConvertTemplate remain identical to previous implementation...
    private static ProtoDataSet ConvertDataSet(PlugBDataSet dataSet)
    {
        var protoDs = new ProtoDataSet { NumOfColumns = (ulong)dataSet.Columns.Count };
        protoDs.Columns.AddRange(dataSet.Columns);
        protoDs.Types_.AddRange(dataSet.Types.Select(t => (uint)t));
        foreach (var row in dataSet.Rows)
        {
            var protoRow = new ProtoDataSet.Types.Row();
            for (int i = 0; i < row.Elements.Count; i++)
            {
                var elementValue = row.Elements[i];
                var elementType = i < dataSet.Types.Count ? dataSet.Types[i] : PlugBDataType.Unknown;
                var dsValue = new ProtoDataSet.Types.DataSetValue();
                if (elementValue != null)
                {
                    switch (elementType)
                    {
                        case PlugBDataType.Int8:
                        case PlugBDataType.Int16:
                        case PlugBDataType.Int32: dsValue.IntValue = Convert.ToUInt32(elementValue); break;
                        case PlugBDataType.Int64: dsValue.LongValue = (ulong)Convert.ToInt64(elementValue); break;
                        case PlugBDataType.UInt8:
                        case PlugBDataType.UInt16:
                        case PlugBDataType.UInt32:
                        case PlugBDataType.UInt64: dsValue.LongValue = Convert.ToUInt64(elementValue); break;
                        case PlugBDataType.DateTime:
                            if (elementValue is DateTime dt) dsValue.LongValue = (ulong)((DateTimeOffset)dt).ToUnixTimeMilliseconds();
                            else if (elementValue is DateTimeOffset dto) dsValue.LongValue = (ulong)dto.ToUnixTimeMilliseconds();
                            else dsValue.LongValue = Convert.ToUInt64(elementValue); break;
                        case PlugBDataType.Float: dsValue.FloatValue = Convert.ToSingle(elementValue); break;
                        case PlugBDataType.Double: dsValue.DoubleValue = Convert.ToDouble(elementValue); break;
                        case PlugBDataType.Boolean: dsValue.BooleanValue = Convert.ToBoolean(elementValue); break;
                        case PlugBDataType.String:
                        case PlugBDataType.Text:
                        case PlugBDataType.Uuid: dsValue.StringValue = elementValue.ToString() ?? string.Empty; break;
                        default: throw new NotSupportedException($"DataType {elementType} is not supported in DataSet elements.");
                    }
                }
                protoRow.Elements.Add(dsValue);
            }
            protoDs.Rows.Add(protoRow);
        }
        return protoDs;
    }

    private static ProtoTemplate ConvertTemplate(PlugBTemplate template)
    {
        var protoTpl = new ProtoTemplate
        {
            IsDefinition = template.IsDefinition,
            Version = template.Version ?? string.Empty,
            TemplateRef = template.TemplateRef ?? string.Empty
        };
        foreach (var metric in template.Metrics)
        {
            var protoMetric = new ProtoMetric
            {
                Name = metric.Name,
                Timestamp = (ulong)metric.TimestampMilliseconds,
                IsHistorical = metric.IsHistorical
            };
            if (metric.Alias.HasValue) protoMetric.Alias = metric.Alias.Value;
            if (metric.Properties != null) protoMetric.Properties = ConvertPropertySet(metric.Properties);
            ApplyToProtoMetric(protoMetric, metric);
            protoTpl.Metrics.Add(protoMetric);
        }
        foreach (var param in template.Parameters)
        {
            var protoParam = new ProtoTemplate.Types.Parameter { Name = param.Name, Type = (uint)param.DataType };
            switch (param.DataType)
            {
                case PlugBDataType.Int8:
                case PlugBDataType.Int16:
                case PlugBDataType.Int32: protoParam.IntValue = Convert.ToUInt32(param.Value); break;
                case PlugBDataType.Int64: protoParam.LongValue = (ulong)Convert.ToInt64(param.Value); break;
                case PlugBDataType.UInt8:
                case PlugBDataType.UInt16:
                case PlugBDataType.UInt32:
                case PlugBDataType.UInt64: protoParam.LongValue = Convert.ToUInt64(param.Value); break;
                case PlugBDataType.Float: protoParam.FloatValue = Convert.ToSingle(param.Value); break;
                case PlugBDataType.Double: protoParam.DoubleValue = Convert.ToDouble(param.Value); break;
                case PlugBDataType.Boolean: protoParam.BooleanValue = Convert.ToBoolean(param.Value); break;
                case PlugBDataType.String:
                case PlugBDataType.Text:
                case PlugBDataType.Uuid: protoParam.StringValue = param.Value.ToString() ?? string.Empty; break;
                default: throw new NotSupportedException($"DataType {param.DataType} is not supported in Template parameters.");
            }
            protoTpl.Parameters.Add(protoParam);
        }
        return protoTpl;
    }
}