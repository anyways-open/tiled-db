using System;
using System.Collections.Generic;
using System.Linq;
using OsmSharp.Db.Tiled.Collections;
using OsmSharp.Db.Tiled.OsmTiled.Data;
using OsmSharp.Db.Tiled.OsmTiled.IO;
using OsmSharp.Db.Tiled.Tiles;

namespace OsmSharp.Db.Tiled.OsmTiled
{
    /// <summary> 
    /// Represents a snapshot of OSM data at a given point in time represented by a diff relative to a full copy of the data.
    /// </summary>
    public class OsmTiledDbSnapshot : OsmTiledDbBase
    {
        private readonly Func<long, OsmTiledDbBase> _getBaseDb;
        
        internal OsmTiledDbSnapshot(string path, Func<long, OsmTiledDbBase> getBaseDb, OsmTiledDbMeta? meta = null)
            : base(path, meta ?? OsmTiledDbOperations.LoadDbMeta(path))
        {
            _getBaseDb = getBaseDb;
        }

        private OsmTiledDbBase? _baseDb;

        private OsmTiledDbOsmGeoIndex GetOsmGeoIndex() => OsmTiledDbOperations.LoadOsmGeoIndex(this.Path);
        private IOsmTiledDbTileIndexReadOnly GetTileIndex() => OsmTiledDbOperations.LoadTileIndex(this.Path);
        private OsmTiledLinkedStream GetData() => OsmTiledDbOperations.LoadData(this.Path);
        
        /// <summary>
        /// Gets the base db.
        /// </summary>
        /// <returns>The base database this snapshot depends on.</returns>
        /// <exception cref="Exception"></exception>
        public  OsmTiledDbBase GetBaseDb()
        {
            if (this.Base == null) throw new Exception($"A {nameof(OsmTiledDbSnapshot)} always needs a base db.");
            return _baseDb ??= _getBaseDb(this.Base.Value);
        }

        /// <inheritdoc/>
        public override OsmTiledDbBase? GetPrevious()
        {
            return GetBaseDb();
        }

        /// <inheritdoc/>
        public override OsmTiledDbBase? GetDbForTile((uint x, uint y) tile)
        {         
            using var tileIndex = this.GetTileIndex();

            var pointer = tileIndex.Get(Tile.ToLocalId(tile, this.Zoom));
            if (pointer != OsmTiledDbTileIndex.EmptyTile) return this;
            
            return this.GetBaseDb().GetDbForTile(tile);
        }

        /// <inheritdoc/>
        public override IEnumerable<(uint x, uint y)> GetTiles(bool modifiedOnly = false)
        {
            using var tileIndex = this.GetTileIndex();
            
            if (modifiedOnly)
            {
                foreach (var tileId in tileIndex.GetTiles())
                {
                    yield return Tile.FromLocalId(this.Zoom, tileId);
                }
            }
            else
            {
                var baseTiles = this.GetBaseDb().GetTiles().Select(x => Tile.ToLocalId(x.x, x.y, this.Zoom));
                foreach (var tileId in baseTiles.MergeWhenSorted(tileIndex.GetTiles()))
                {
                    yield return Tile.FromLocalId(this.Zoom, tileId);
                }
            }
        }
        
        /// <inheritdoc/>
        public override IEnumerable<(OsmGeo osmGeo, IEnumerable<(uint x, uint y)> tiles)> Get(IEnumerable<OsmGeoKey>? osmGeoKeys, byte[]? buffer = null)
        {
            buffer ??= new byte[1024];
            using var data = this.GetData();
            using var index = this.GetOsmGeoIndex();
            
            if (osmGeoKeys == null)
            {
                var allHere = data.Get(buffer)
                    .Select(x => (x.osmGeo, x.tile
                        .Select(t => Tile.FromLocalId(this.Zoom, t))));
                var allBase = this.GetBaseDb().Get((IEnumerable<OsmGeoKey>?)null, buffer);
                var merged = allBase.MergeWhenSorted(allHere, (o1, o2) => 
                    (new OsmGeoKey(o1.osmGeo).CompareTo(new OsmGeoKey(o2.osmGeo))));
                foreach (var merge in merged)
                {
                    yield return merge;
                }
                yield break;
            }
            
            // return all data found here first.
            var keysSet = new HashSet<OsmGeoKey>(osmGeoKeys);
            using var osmGeoIndex = this.GetOsmGeoIndex();
            var pointers = osmGeoIndex.GetAll(osmGeoKeys).Where(x =>
            {
                if (x.pointer >= 0) return true;
                
                keysSet.Remove(x.key);
                return false;
            }).Select(x => x.pointer);
            foreach (var osmGeoAndTiles in data.Get(pointers, buffer))
            {
                keysSet.Remove(new OsmGeoKey(osmGeoAndTiles.osmGeo));
                yield return (osmGeoAndTiles.osmGeo,
                    osmGeoAndTiles.tiles.Select(x => Tile.FromLocalId(this.Zoom, x)));
            }

            // returns base data if there are keys left.
            if (keysSet.Count == 0) yield break;
            foreach (var osmGeoAndTiles in this.GetBaseDb().Get(keysSet, buffer))
            {
                yield return osmGeoAndTiles;
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<(OsmGeoKey key, IEnumerable<(uint x, uint y)> tiles)> GetTilesFor(IEnumerable<OsmGeoKey> osmGeoKeys)
        {
            foreach (var osmGeoKey in osmGeoKeys)
            {
                yield return (osmGeoKey, this.GetTilesFor(osmGeoKey));
            }
        }

        private IEnumerable<(uint x, uint y)> GetTilesFor(OsmGeoKey key)
        {  
            using var data = this.GetData();
            using var osmGeoIndex = this.GetOsmGeoIndex();
            
            var pointer = osmGeoIndex.Get(key);
            switch (pointer)
            {
                case -1:
                    // object was deleted!
                    yield break;
                case null:
                {
                    // attempt getting data from base db.
                    foreach (var osmGeo in this.GetBaseDb().GetTilesFor(new [] { key }))
                    {
                        foreach (var tile in osmGeo.tiles)
                        {
                            yield return tile;
                        }
                    }

                    break;
                }
                default:
                {
                    // object is here, get it locally.
                    foreach (var tileId in data.GetTilesFor(pointer.Value))
                    {
                        yield return Tile.FromLocalId(this.Zoom, tileId);
                    }

                    break;
                }
            }
        }

        private IEnumerable<(OsmGeo osmGeo, IEnumerable<uint> tiles)> GetLocal(byte[] buffer)
        {
            using var data = this.GetData();
            
            return data.Get(buffer);
        }

        /// <inheritdoc/>
        public override IEnumerable<(OsmGeo osmGeo, IEnumerable<(uint x, uint y)> tiles)> Get(
            byte[]? buffer = null)
        {
            buffer ??= new byte[1024];

            // data in both, merge the two.
            using var baseEnumerator = this.GetBaseDb().Get(buffer).GetEnumerator();
            using var thisEnumerator = this.GetLocal(buffer).GetEnumerator();
            var baseHasNext = baseEnumerator.MoveNext();
            var thisHasNext = thisEnumerator.MoveNext();

            while (baseHasNext || thisHasNext)
            {
                if (baseHasNext && thisHasNext)
                {
                    var baseKey = new OsmGeoKey(baseEnumerator.Current.osmGeo);
                    var thisKey = new OsmGeoKey(thisEnumerator.Current.osmGeo);

                    if (baseKey < thisKey)
                    {
                        yield return baseEnumerator.Current;
                        baseHasNext = baseEnumerator.MoveNext();
                    }
                    else if (thisKey < baseKey)
                    {
                        var (osmGeo, osmGeoTiles) = thisEnumerator.Current;
                        yield return (osmGeo,
                            osmGeoTiles.Select(x => Tile.FromLocalId(this.Zoom, x)).ToArray());
                        thisHasNext = thisEnumerator.MoveNext();
                    }
                    else
                    {
                        var (osmGeo, osmGeoTiles) = thisEnumerator.Current;
                        yield return (osmGeo,
                            osmGeoTiles.Select(x => Tile.FromLocalId(this.Zoom, x)).ToArray());
                        baseHasNext = baseEnumerator.MoveNext();
                        thisHasNext = thisEnumerator.MoveNext();
                    }
                }
                else if (baseHasNext)
                {
                    yield return baseEnumerator.Current;
                    baseHasNext = baseEnumerator.MoveNext();
                }
                else
                {
                    var (osmGeo, osmGeoTiles) = thisEnumerator.Current;
                    yield return (osmGeo, osmGeoTiles.Select(x => Tile.FromLocalId(this.Zoom, x)).ToArray());
                    thisHasNext = thisEnumerator.MoveNext();
                }
            }
        }

        private IEnumerable<(OsmGeo osmGeo, IEnumerable<uint> tiles)> GetLocal(
            IEnumerable<uint> tiles, byte[] buffer)
        {
            using var tileIndex = this.GetTileIndex();
            using var data = this.GetData();
            
            var lowestTilePointers = tileIndex.LowestPointersFor(tiles).ToList();
            if (lowestTilePointers.Count == 0) yield break;
            
            foreach (var (osmGeo, osmGeoTiles) in data.GetForTiles(lowestTilePointers, tiles,
                buffer))
            {
                yield return (osmGeo, osmGeoTiles);
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<(OsmGeo osmGeo, IEnumerable<(uint x, uint y)> tiles)> Get(IEnumerable<(uint x, uint y)> tiles, byte[]? buffer = null)
        {
            buffer ??= new byte[1024];
            using var tileIndex = this.GetTileIndex();
            using var data = this.GetData();
            
            if (tiles.HasOne(out var only))
            {
                var tileId = Tile.ToLocalId(only, this.Zoom);
                var tileIsLocal = tileIndex.HasTile(tileId);
                if (tileIsLocal)
                {
                    // tile is in this diff.
                    foreach (var (osmGeo, osmGeoTiles) in this.GetLocal(tiles.Select(x => Tile.ToLocalId(x, this.Zoom)), buffer))
                    {
                        yield return (osmGeo, osmGeoTiles.Select(x => Tile.FromLocalId(this.Zoom, x)));
                    }
                }
                else
                {
                    // tile is not in diff.
                    foreach (var t in this.GetBaseDb().Get(tiles, buffer))
                    {
                        yield return t;
                    }
                }
            }
            else
            {
                var localTiles = new List<(uint x, uint y)>();
                var otherTiles = new List<(uint x, uint y)>();
                foreach (var tile in tiles)
                {
                    if (tileIndex.HasTile(Tile.ToLocalId(tile, this.Zoom)))
                    {
                        localTiles.Add(tile);
                    }
                    else
                    {
                        otherTiles.Add(tile);
                    }
                }

                if (localTiles.Count == 0)
                {
                    // no tiles is in this diff.
                    foreach (var t in this.GetBaseDb().Get(otherTiles, buffer))
                    {
                        yield return t;
                    }

                    yield break;
                }

                if (otherTiles.Count == 0)
                {
                    foreach (var (osmGeo, osmGeoTiles) in this.GetLocal(tiles.Select(x => Tile.ToLocalId(x, this.Zoom)), buffer))
                    {
                        yield return (osmGeo, osmGeoTiles.Select(x => Tile.FromLocalId(this.Zoom, x)));
                    }
                    yield break;
                }

                // data in both, merge the two.
                using var baseEnumerator = this.GetBaseDb().Get(otherTiles, buffer).GetEnumerator();
                using var thisEnumerator = this.GetLocal(tiles.Select(x => Tile.ToLocalId(x, this.Zoom)),
                    buffer).GetEnumerator();
                var baseHasNext = baseEnumerator.MoveNext();
                var thisHasNext = thisEnumerator.MoveNext();

                while (baseHasNext || thisHasNext)
                {
                    if (baseHasNext && thisHasNext)
                    {
                        var baseKey = new OsmGeoKey(baseEnumerator.Current.osmGeo);
                        var thisKey = new OsmGeoKey(thisEnumerator.Current.osmGeo);

                        if (baseKey < thisKey)
                        {
                            yield return baseEnumerator.Current;
                            baseHasNext = baseEnumerator.MoveNext();
                        }
                        else if (thisKey < baseKey)
                        {
                            var (osmGeo, osmGeoTiles) = thisEnumerator.Current;
                            yield return (osmGeo,
                                osmGeoTiles.Select(x => Tile.FromLocalId(this.Zoom, x)).ToArray());
                            thisHasNext = thisEnumerator.MoveNext();
                        }
                        else
                        {
                            var (osmGeo, osmGeoTiles) = thisEnumerator.Current;
                            yield return (osmGeo,
                                osmGeoTiles.Select(x => Tile.FromLocalId(this.Zoom, x)).ToArray());
                            baseHasNext = baseEnumerator.MoveNext();
                            thisHasNext = thisEnumerator.MoveNext();
                        }
                    }
                    else if (baseHasNext)
                    {
                        yield return baseEnumerator.Current;
                        baseHasNext = baseEnumerator.MoveNext();
                    }
                    else
                    {
                        var (osmGeo, osmGeoTiles) = thisEnumerator.Current;
                        yield return (osmGeo, osmGeoTiles.Select(x => Tile.FromLocalId(this.Zoom, x)).ToArray());
                        thisHasNext = thisEnumerator.MoveNext();
                    }
                }
            }
        }
    }
}