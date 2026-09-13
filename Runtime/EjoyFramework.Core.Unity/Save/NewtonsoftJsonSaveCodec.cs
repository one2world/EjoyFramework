//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Text;
using EjoyFramework.Core.Save;
using Newtonsoft.Json;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Production JSON codec for ESV2 payloads. Supports dictionaries, nested collections and ordinary POCO fields.
    /// TypeNameHandling is intentionally disabled; persisted types are selected by the generic Load call.
    /// </summary>
    public sealed class NewtonsoftJsonSaveCodec : ISaveCodec
    {
        public const string CodecId = "newtonsoft-json-v1";

        private static readonly JsonSerializerSettings s_Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None,
        };

        public string Id { get { return CodecId; } }

        public byte[] Encode(object value, Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            string json = JsonConvert.SerializeObject(value, type, s_Settings);
            return Encoding.UTF8.GetBytes(json ?? "null");
        }

        public object Decode(byte[] payload, Type type)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (type == null) throw new ArgumentNullException(nameof(type));
            string json = Encoding.UTF8.GetString(payload);
            return JsonConvert.DeserializeObject(json, type, s_Settings);
        }
    }
}
