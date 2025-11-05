// Copyright (c) Microsoft. All rights reserved.

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>
/// Represents a key-value memo with ETag-based concurrency control.
/// </summary>
public sealed class Memo : Dictionary<string, string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Memo"/> class.
    /// </summary>
    public Memo()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Memo"/> class with the specified ETag.
    /// </summary>
    /// <param name="eTag">The ETag for concurrency control.</param>
    public Memo(string? eTag)
    {
        this.ETag = eTag;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Memo"/> class with the specified data and ETag.
    /// </summary>
    /// <param name="data">The initial key-value pairs.</param>
    /// <param name="eTag">The ETag for concurrency control.</param>
    public Memo(IDictionary<string, string> data, string? eTag)
        : base(data)
    {
        this.ETag = eTag;
    }

    /// <summary>
    /// Gets or sets the ETag for optimistic concurrency control.
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// Adds a key-value pair to the memo if the key does not already exist, or updates the existing value.
    /// </summary>
    /// <param name="key">The key of the element to add or update.</param>
    /// <param name="addValue">The value to be added if the key does not exist.</param>
    /// <param name="updateValueFactory">The function used to generate a new value based on the key's existing value.</param>
    /// <returns>The new value for the key.</returns>
    public string AddOrUpdate(string key, string addValue, Func<string, string, string> updateValueFactory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(updateValueFactory);

        if (this.TryGetValue(key, out string? existingValue))
        {
            string newValue = updateValueFactory(key, existingValue);
            this[key] = newValue;
            return newValue;
        }

        this[key] = addValue;
        return addValue;
    }

    /// <summary>
    /// Adds a key-value pair to the memo if the key does not already exist, or updates the existing value.
    /// </summary>
    /// <param name="key">The key of the element to add or update.</param>
    /// <param name="addValueFactory">The function used to generate a value for the key if it does not exist.</param>
    /// <param name="updateValueFactory">The function used to generate a new value based on the key's existing value.</param>
    /// <returns>The new value for the key.</returns>
    public string AddOrUpdate(string key, Func<string, string> addValueFactory, Func<string, string, string> updateValueFactory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(addValueFactory);
        ArgumentNullException.ThrowIfNull(updateValueFactory);

        if (this.TryGetValue(key, out string? existingValue))
        {
            string updatedValue = updateValueFactory(key, existingValue);
            this[key] = updatedValue;
            return updatedValue;
        }

        string addedValue = addValueFactory(key);
        this[key] = addedValue;
        return addedValue;
    }

    /// <summary>
    /// Adds a key-value pair to the memo if the key does not already exist, or updates the existing value.
    /// </summary>
    /// <typeparam name="TArg">The type of the argument to pass to the factory functions.</typeparam>
    /// <param name="key">The key of the element to add or update.</param>
    /// <param name="addValueFactory">The function used to generate a value for the key if it does not exist.</param>
    /// <param name="updateValueFactory">The function used to generate a new value based on the key's existing value.</param>
    /// <param name="factoryArgument">The argument to pass to the factory functions.</param>
    /// <returns>The new value for the key.</returns>
    public string AddOrUpdate<TArg>(string key, Func<string, TArg, string> addValueFactory, Func<string, string, TArg, string> updateValueFactory, TArg factoryArgument)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(addValueFactory);
        ArgumentNullException.ThrowIfNull(updateValueFactory);

        if (this.TryGetValue(key, out string? existingValue))
        {
            string updatedValue = updateValueFactory(key, existingValue, factoryArgument);
            this[key] = updatedValue;
            return updatedValue;
        }

        string addedValue = addValueFactory(key, factoryArgument);
        this[key] = addedValue;
        return addedValue;
    }

    /// <summary>
    /// Adds a key-value pair to the memo if the key does not already exist.
    /// </summary>
    /// <param name="key">The key of the element to add.</param>
    /// <param name="value">The value to be added if the key does not exist.</param>
    /// <returns>The value for the key. This will be either the existing value if the key is already in the memo, or the new value if the key was not in the memo.</returns>
    public string GetOrAdd(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (this.TryGetValue(key, out string? existingValue))
        {
            return existingValue;
        }

        this[key] = value;
        return value;
    }

    /// <summary>
    /// Adds a key-value pair to the memo by using the specified function if the key does not already exist.
    /// </summary>
    /// <param name="key">The key of the element to add.</param>
    /// <param name="valueFactory">The function used to generate a value for the key.</param>
    /// <returns>The value for the key. This will be either the existing value if the key is already in the memo, or the new value if the key was not in the memo.</returns>
    public string GetOrAdd(string key, Func<string, string> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (this.TryGetValue(key, out string? existingValue))
        {
            return existingValue;
        }

        string newValue = valueFactory(key);
        this[key] = newValue;
        return newValue;
    }

    /// <summary>
    /// Adds a key-value pair to the memo by using the specified function if the key does not already exist.
    /// </summary>
    /// <typeparam name="TArg">The type of the argument to pass to the value factory.</typeparam>
    /// <param name="key">The key of the element to add.</param>
    /// <param name="valueFactory">The function used to generate a value for the key.</param>
    /// <param name="factoryArgument">The argument to pass to the value factory.</param>
    /// <returns>The value for the key. This will be either the existing value if the key is already in the memo, or the new value if the key was not in the memo.</returns>
    public string GetOrAdd<TArg>(string key, Func<string, TArg, string> valueFactory, TArg factoryArgument)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (this.TryGetValue(key, out string? existingValue))
        {
            return existingValue;
        }

        string newValue = valueFactory(key, factoryArgument);
        this[key] = newValue;
        return newValue;
    }

    /// <summary>
    /// Attempts to add the specified key and value to the memo.
    /// </summary>
    /// <param name="key">The key of the element to add.</param>
    /// <param name="value">The value of the element to add.</param>
    /// <returns><see langword="true"/> if the key-value pair was added successfully; otherwise, <see langword="false"/>.</returns>
    public new bool TryAdd(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (this.ContainsKey(key))
        {
            return false;
        }

        this[key] = value;
        return true;
    }

    /// <summary>
    /// Updates the value associated with the specified key if the key exists.
    /// </summary>
    /// <param name="key">The key of the value to update.</param>
    /// <param name="newValue">The new value to set.</param>
    /// <param name="comparisonValue">The value that is compared to the value of the element with key.</param>
    /// <returns><see langword="true"/> if the value with key was equal to comparisonValue and was replaced with newValue; otherwise, <see langword="false"/>.</returns>
    public bool TryUpdate(string key, string newValue, string comparisonValue)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (this.TryGetValue(key, out string? existingValue) && existingValue == comparisonValue)
        {
            this[key] = newValue;
            return true;
        }

        return false;
    }
}
