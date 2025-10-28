using System.Reflection;
using MongoDB.Bson.Serialization;
using KaiAssistant.Domain.Entities.Experiences;

namespace KaiAssistant.Infrastructure.Mongo;

public static class MongoClassMapRegistrar
{
    public static void RegisterClassMaps()
    {
        if (!BsonClassMap.IsClassMapRegistered(typeof(Experience)))
        {
            BsonClassMap.RegisterClassMap<Experience>(cm =>
            {
                cm.AutoMap();

                cm.MapMember(x => x.BulletPoints).SetElementName("BulletPoints");

                var field = typeof(Experience).GetField("_bulletPoints", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    cm.MapField(field.Name).SetElementName("BulletPoints");
                }

                cm.SetIgnoreExtraElements(true);
            });
        }
    }
}
