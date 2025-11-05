// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel.DataAnnotations;

namespace AgentContracts;

/// <summary>
/// Validation attribute that ensures a string value is a valid absolute URI.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class AbsoluteUriAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            // Use [Required] for null checks
            return ValidationResult.Success;
        }

        if (value is not string stringValue)
        {
            return new ValidationResult("Value must be a string.");
        }

        if (string.IsNullOrWhiteSpace(stringValue))
        {
            return new ValidationResult("URI cannot be empty or whitespace.");
        }

        if (!Uri.TryCreate(stringValue, UriKind.Absolute, out _))
        {
            return new ValidationResult(this.ErrorMessage ?? $"The value '{stringValue}' is not a valid absolute URI.");
        }

        return ValidationResult.Success;
    }
}
