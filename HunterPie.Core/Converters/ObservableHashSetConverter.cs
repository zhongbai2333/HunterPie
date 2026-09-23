using HunterPie.Core.Architecture;
using Newtonsoft.Json;
using System;
using System.Linq;

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
        // Remove and add items individually so configuration observers can rebind them.
        foreach (T item in existingCollection.ToArray())
            existingCollection.Remove(item);

        // Reset the empty set's free slots so the restored items keep their serialized order.
        existingCollection.Clear();

        foreach (T item in hashSet)
            existingCollection.Add(item);

        return existingCollection;
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(ObservableHashSet<T>);
}