using PlugB.Options;

namespace PlugB.Builders;

/// <summary>
/// Fluent Builder to create Sparkplug-B Metrics cleanly.
/// </summary>
public class MetricBuilder
{
    private readonly string _name;
    private ulong? _alias;
    private PlugBDataType _dataType = PlugBDataType.Unknown;
    private object? _value;
    private long _timestamp;
    private bool _isHistorical;
    private PlugBPropertySet? _properties;

    private MetricBuilder(string name)
    {
        _name = name;
        _timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public static MetricBuilder Create(string name) => new(name);

    public MetricBuilder WithAlias(ulong alias)
    {
        _alias = alias;
        return this;
    }

    public MetricBuilder WithValue(object value, PlugBDataType dataType)
    {
        _value = value;
        _dataType = dataType;
        return this;
    }

    public MetricBuilder WithValue(int value) => WithValue(value, PlugBDataType.Int32);

    public MetricBuilder WithValue(float value) => WithValue(value, PlugBDataType.Float);

    public MetricBuilder WithValue(double value) => WithValue(value, PlugBDataType.Double);

    public MetricBuilder WithValue(bool value) => WithValue(value, PlugBDataType.Boolean);

    public MetricBuilder WithValue(string value) => WithValue(value, PlugBDataType.String);

    public MetricBuilder WithTimestamp(long timestampMilliseconds)
    {
        _timestamp = timestampMilliseconds;
        return this;
    }

    public MetricBuilder IsHistorical(bool isHistorical = true)
    {
        _isHistorical = isHistorical;
        return this;
    }

    public MetricBuilder WithProperty(string key, PlugBDataType dataType, object? value)
    {
        _properties ??= new PlugBPropertySet();
        _properties.AddProperty(key, dataType, value);
        return this;
    }

    public Metric Build()
    {
        return new Metric
        {
            Name = _name,
            Alias = _alias,
            DataType = _dataType,
            Value = _value ?? throw new InvalidOperationException($"Value for metric '{_name}' must be set."),
            TimestampMilliseconds = _timestamp,
            IsHistorical = _isHistorical,
            Properties = _properties
        };
    }
}