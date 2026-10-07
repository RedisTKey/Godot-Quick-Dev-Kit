using System;
using Godot;
using Godot.Collections;

namespace QuickDev.Database;

/// <summary>A user-defined, versioned save document. No game-specific fields are assumed.</summary>
public partial class QuickDatabaseDocument : RefCounted
{
    public const long SchemaVersion = 1;
    public virtual long GetSchemaVersion() => SchemaVersion;
    public virtual Dictionary ToDictionary() => new();
    public virtual void FromDictionary(Dictionary data) { }

    /// <summary>Override for documents without a public parameterless constructor.</summary>
    public virtual QuickDatabaseDocument DuplicateDocument()
    {
        var copy = Activator.CreateInstance(GetType()) as QuickDatabaseDocument
            ?? throw new InvalidOperationException("The document must have a public parameterless constructor or override DuplicateDocument().");
        // Isolate nested Godot arrays/dictionaries even when a document exposes its fields directly.
        copy.FromDictionary(ToDictionary().Duplicate(true));
        return copy;
    }
}
