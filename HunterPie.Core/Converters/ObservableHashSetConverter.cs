using HunterPie.Core.Architecture;
using Newtonsoft.Json;
using System;

namespace HunterPie.Core.Converters;

public class ObservableHashSetConverter<T> : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
        JsonSerializer.CreateDefault().Serialize(writer, value);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var existingCollection = (ObservableHashSet<T>)existingValue!;
        var hashSet = (ObservableHashSet<T>?)JsonSerializer.CreateDefault().Deserialize(reader, objectType);

        if (hashSet is not { })
            return existingCollection;

        // Equal keys may still contain different settings (for example monster parts).
        // Replace the items while keeping the bound collection instance.
        existingCollection.Clear();
        existingCollection.UnionWith(hashSet);

        return existingCollection;
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(ObservableHashSet<T>);
}
