using PlugB.Abstractions;
using System.Collections.Concurrent;

namespace PlugB.Internal.State;

/// <summary>
/// Maintains the state of all registered sub-devices for the Edge Node.
/// Tracks whether a device needs a DBIRTH message published.
/// </summary>
internal class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceRegistration> _devices = new();

    /// <summary>
    /// Registers a new device to the Edge Node.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if a device with the same ID already exists.</exception>
    public void RegisterDevice(IPlugBDevice device)
    {
        var registration = new DeviceRegistration(device);
        if (!_devices.TryAdd(device.DeviceId, registration))
        {
            throw new ArgumentException($"Device with ID '{device.DeviceId}' is already registered.");
        }
    }

    public IPlugBDevice? GetDevice(string deviceId)
    {
        return _devices.TryGetValue(deviceId, out var reg) ? reg.Device : null;
    }

    public IEnumerable<IPlugBDevice> GetAllDevices()
    {
        return _devices.Values.Select(r => r.Device);
    }

    /// <summary>
    /// Resets the birth state for all devices.
    /// Called upon (re)connection, meaning all devices will require a new DBIRTH.
    /// </summary>
    public void MarkAllBirthsAsUnsent()
    {
        foreach (var reg in _devices.Values)
        {
            reg.IsBirthSent = false;
        }
    }

    /// <summary>
    /// Checks if a specific device requires a DBIRTH to be sent.
    /// </summary>
    public bool RequiresBirth(string deviceId)
    {
        if (_devices.TryGetValue(deviceId, out var reg))
        {
            return !reg.IsBirthSent;
        }
        return false;
    }

    /// <summary>
    /// Marks the DBIRTH as successfully sent for a device.
    /// </summary>
    public void MarkBirthSent(string deviceId)
    {
        if (_devices.TryGetValue(deviceId, out var reg))
        {
            reg.IsBirthSent = true;
        }
    }

    private class DeviceRegistration(IPlugBDevice device)
    {
        public IPlugBDevice Device { get; } = device;
        public bool IsBirthSent { get; set; } = false;
    }
}