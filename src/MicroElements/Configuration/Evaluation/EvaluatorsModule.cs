﻿using MicroElements.DependencyInjection;

namespace MicroElements.Bootstrap.Extensions.Configuration.Evaluation
{
    [BuildStep("Evaluators")]
    public class EvaluatorsModule : IBuildStep
    {
        public void Execute(BuildContext buildContext)
        {
            buildContext.ServiceCollection.AddSingletons<IValueEvaluator>(buildContext.ExportedTypes);
        }
    }
}