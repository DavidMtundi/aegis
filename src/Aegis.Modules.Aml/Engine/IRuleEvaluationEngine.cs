using System;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Aml.Domain;

namespace Aegis.Modules.Aml.Engine;

public interface IRuleEvaluationEngine
{
    Task<RuleEvaluationResult> EvaluateAsync(
        AmlRuleVersion ruleVersion,
        IFeatureContext features,
        string focusEntityId,
        string focusEntityType,
        CancellationToken cancellationToken = default);
}
