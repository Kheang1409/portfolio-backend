using KaiAssistant.Domain.Entities.Resumes;
using KaiAssistant.Domain.Interfaces.Repositories;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace KaiAssistant.Infrastructure.Persistence.Repositories;

public class ResumeRepository : IResumeRepository
{
    private readonly IMongoCollection<Resume> _collection;
    private readonly IMongoCollection<BsonDocument> _bsonCollection;

    public ResumeRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<Resume>("resumes");
        _bsonCollection = database.GetCollection<BsonDocument>("resumes");
    }

    public async Task<Resume?> GetByIdAsync(string id)
    {
    var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
    var doc = await _bsonCollection.Find(filter).FirstOrDefaultAsync();
        if (doc == null) return null;
        NormalizeExperienceBulletPoints(doc);
        return BsonSerializer.Deserialize<Resume>(doc);
    }

    public async Task<Resume?> GetLatestAsync()
    {
    var doc = await _bsonCollection.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefaultAsync();
        if (doc == null) return null;
        NormalizeExperienceBulletPoints(doc);
        return BsonSerializer.Deserialize<Resume>(doc);
    }

    private void NormalizeExperienceBulletPoints(BsonDocument doc)
    {
        if (!doc.Contains("experiences")) return;
        var experiences = doc.GetValue("experiences").AsBsonArray;
        for (int i = 0; i < experiences.Count; i++)
        {
            if (!experiences[i].IsBsonDocument) continue;
            var expDoc = experiences[i].AsBsonDocument;

            
            if (expDoc.Contains("BulletPoint") && !expDoc.Contains("BulletPoints"))
            {
                expDoc["BulletPoints"] = expDoc.GetValue("BulletPoint");
                expDoc.Remove("BulletPoint");
            }

            
            if (expDoc.Contains("bulletPoint") && !expDoc.Contains("BulletPoints"))
            {
                expDoc["BulletPoints"] = expDoc.GetValue("bulletPoint");
                expDoc.Remove("bulletPoint");
            }

            if (expDoc.Contains("bulletPoints") && !expDoc.Contains("BulletPoints"))
            {
                expDoc["BulletPoints"] = expDoc.GetValue("bulletPoints");
                expDoc.Remove("bulletPoints");
            }

            experiences[i] = expDoc;
        }
        doc["experiences"] = experiences;
    }

    public async Task InsertAsync(Resume resume)
    {
        if (resume == null) throw new ArgumentNullException(nameof(resume));
        await _collection.InsertOneAsync(resume);
    }
}
