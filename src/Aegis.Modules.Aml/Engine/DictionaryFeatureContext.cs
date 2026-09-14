using System;
using System.Collections.Generic;
using System.Linq;

namespace Aegis.Modules.Aml.Engine;

public class DictionaryFeatureContext : IFeatureContext
{
    private readonly Dictionary<string, object> _features;

    public DictionaryFeatureContext(IDictionary<string, object> features)
    {
        _features = new Dictionary<string, object>(features ?? new Dictionary<string, object>(), StringComparer.OrdinalIgnoreCase);
    }

    public decimal GetDecimal(string featureName)
    {
        if (_features.TryGetValue(featureName, out var value) && value != null)
        {
            return Convert.ToDecimal(value);
        }
        return 0m;
    }

    public long GetLong(string featureName)
    {
        if (_features.TryGetValue(featureName, out var value) && value != null)
        {
            return Convert.ToInt64(value);
        }
        return 0L;
    }

    public bool GetBool(string featureName)
    {
        if (_features.TryGetValue(featureName, out var value) && value != null)
        {
            return Convert.ToBoolean(value);
        }
        return false;
    }

    public string? GetString(string featureName)
    {
        if (_features.TryGetValue(featureName, out var value) && value != null)
        {
            return value.ToString();
        }
        return null;
    }

    public bool HasFeature(string featureName)
    {
        return _features.ContainsKey(featureName);
    }

    public IReadOnlyDictionary<string, object> GetAll()
    {
        return _features.ToDictionary(k => k.Key, v => v.Value);
    }
}
