using System.Collections.Generic;

namespace Aegis.Modules.Aml.Engine;

public interface IFeatureContext
{
    decimal GetDecimal(string featureName);
    long GetLong(string featureName);
    bool GetBool(string featureName);
    string? GetString(string featureName);
    bool HasFeature(string featureName);
    IReadOnlyDictionary<string, object> GetAll();
}
