using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Interfaces.Repositories;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace KaiAssistant.Infrastructure.Persistence;
public class MongoRepository<T> : IRepository<T> where T : class
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoRepository(IMongoDatabase database)
    {
        var name = typeof(T).Name.ToLowerInvariant() + "s";
        _collection = database.GetCollection<BsonDocument>(name);
    }

    public async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var docs = await _collection.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(cancellationToken).ConfigureAwait(false);
        return docs.Select(d => BsonSerializer.Deserialize<T>(d));
    }

    public async Task<IEnumerable<T>> GetAllAsync(IUnitOfWorkSession? uowSession, CancellationToken cancellationToken = default)
    {
        var session = uowSession?.NativeSession as IClientSessionHandle;
        var docs = session == null
            ? await _collection.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(cancellationToken).ConfigureAwait(false)
            : await _collection.Find(session, Builders<BsonDocument>.Filter.Empty).ToListAsync(cancellationToken).ConfigureAwait(false);
        return docs.Select(d => BsonSerializer.Deserialize<T>(d));
    }

    public async Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (ObjectId.TryParse(id, out var oid))
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", oid);
            var doc = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (doc == null) return null;
            return BsonSerializer.Deserialize<T>(doc);
        }

        var f2 = Builders<BsonDocument>.Filter.Eq("_id", id);
        var doc2 = await _collection.Find(f2).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (doc2 == null) return null;
        return BsonSerializer.Deserialize<T>(doc2);
    }

    public async Task<T?> GetByIdAsync(string id, IUnitOfWorkSession? uowSession, CancellationToken cancellationToken = default)
    {
        FilterDefinition<BsonDocument> filter = ObjectId.TryParse(id, out var oid)
            ? Builders<BsonDocument>.Filter.Eq("_id", oid)
            : Builders<BsonDocument>.Filter.Eq("_id", id);
        var session = uowSession?.NativeSession as IClientSessionHandle;
        var doc = session == null
            ? await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : await _collection.Find(session, filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (doc == null) return null;
        return BsonSerializer.Deserialize<T>(doc);
    }

    public async Task InsertAsync(T entity, CancellationToken cancellationToken = default)
    {
        var doc = entity switch
        {
            BsonDocument bd => bd,
            _ => entity.ToBsonDocument()
        };
        await _collection.InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertAsync(T entity, IUnitOfWorkSession? uowSession, CancellationToken cancellationToken = default)
    {
        var doc = entity switch
        {
            BsonDocument bd => bd,
            _ => entity.ToBsonDocument()
        };
        var session = uowSession?.NativeSession as IClientSessionHandle;
        if (session == null)
        {
            await _collection.InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _collection.InsertOneAsync(session, doc, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ReplaceAsync(string id, T entity, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out var oid))
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", id);
            var doc = entity.ToBsonDocument();
            await _collection.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = false }, cancellationToken).ConfigureAwait(false);
            return;
        }
        var filterOid = Builders<BsonDocument>.Filter.Eq("_id", oid);
        var docOid = entity.ToBsonDocument();
        await _collection.ReplaceOneAsync(filterOid, docOid, new ReplaceOptions { IsUpsert = false }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceAsync(string id, T entity, IUnitOfWorkSession? uowSession, CancellationToken cancellationToken = default)
    {
        var session = uowSession?.NativeSession as IClientSessionHandle;
        var filter = ObjectId.TryParse(id, out var oid)
            ? Builders<BsonDocument>.Filter.Eq("_id", oid)
            : Builders<BsonDocument>.Filter.Eq("_id", id);
        var doc = entity.ToBsonDocument();
        if (session == null)
            await _collection.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = false }, cancellationToken).ConfigureAwait(false);
        else
            await _collection.ReplaceOneAsync(session, filter, doc, new ReplaceOptions { IsUpsert = false }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        FilterDefinition<BsonDocument> filter;
        if (ObjectId.TryParse(id, out var oid)) filter = Builders<BsonDocument>.Filter.Eq("_id", oid);
        else filter = Builders<BsonDocument>.Filter.Eq("_id", id);
        await _collection.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, IUnitOfWorkSession? uowSession, CancellationToken cancellationToken = default)
    {
        FilterDefinition<BsonDocument> filter;
        if (ObjectId.TryParse(id, out var oid)) filter = Builders<BsonDocument>.Filter.Eq("_id", oid);
        else filter = Builders<BsonDocument>.Filter.Eq("_id", id);
        var session = uowSession?.NativeSession as IClientSessionHandle;
        if (session == null)
            await _collection.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false);
        else
            await _collection.DeleteOneAsync(session, filter, null, cancellationToken).ConfigureAwait(false);
    }
}