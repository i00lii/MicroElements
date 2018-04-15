﻿// Copyright (c) MicroElements. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace MicroElements.Configuration.Evaluation
{
    /// <summary>
    /// Вычисление выражений вида ${environment:envVarName}.
    /// Выражение заменяется на Environment Variable.
    /// </summary>
    public class EnvironmentEvaluator : IValueEvaluator
    {
        /// <inheritdoc />
        public string Name => "environment";

        /// <inheritdoc />
        public bool TryEvaluate(string expression, out string value)
        {
            value = Environment.GetEnvironmentVariable(expression);
            return value != null;
        }
    }
}
