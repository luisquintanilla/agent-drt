// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using Orleans.Storage;

namespace AgentGateway.Utilities;

/// <summary>
/// Grain storage serializer that uses System.Text.Json.
/// </summary>
public sealed class SystemTextJsonGrainStorageSerializer : IGrainStorageSerializer
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTextJsonGrainStorageSerializer"/> class.
    /// </summary>
    /// <param name="jsonSerializerOptions">The JSON serializer options to use for serialization.</param>
    public SystemTextJsonGrainStorageSerializer(JsonSerializerOptions jsonSerializerOptions)
    {
        this._jsonSerializerOptions = jsonSerializerOptions;
    }

    /// <inheritdoc/>
    public BinaryData Serialize<T>(T input)
    {
        // Get the JsonTypeInfo<T> from options for better AOT compatibility
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)this._jsonSerializerOptions.GetTypeInfo(typeof(T));
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(input, typeInfo);
        return new BinaryData(jsonBytes);
    }

    /// <inheritdoc/>
    public T Deserialize<T>(BinaryData input)
    {
        // Get the JsonTypeInfo<T> from options for better AOT compatibility
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)this._jsonSerializerOptions.GetTypeInfo(typeof(T));
        return JsonSerializer.Deserialize(input.ToMemory().Span, typeInfo)
            ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}");
    }
}
