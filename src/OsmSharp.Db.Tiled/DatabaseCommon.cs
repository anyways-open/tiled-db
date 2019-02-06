﻿using OsmSharp.Db.Tiled.Indexes;
using OsmSharp.Db.Tiled.Tiles;
using OsmSharp.Db.Tiled.IO;
using System.IO;
using System.IO.Compression;
using Reminiscence.Arrays;

namespace OsmSharp.Db.Tiled
{
    internal static class DatabaseCommon
    {
        private static Stream CreateInflateStream(Stream stream)
        {
            //return new DeflateStream(stream, CompressionMode.Decompress);
            return new GZipStream(stream, CompressionMode.Decompress);
        }

        private static Stream CreateDeflateStream(Stream stream)
        {
            //return new DeflateStream(stream, CompressionLevel.Fastest);
            return new GZipStream(stream, CompressionLevel.Fastest);
        }
        
        /// <summary>
        /// Loads one tile.
        /// </summary>
        public static Stream LoadTile(string path, OsmGeoType type, Tile tile, bool compressed = false)
        {
            var location = DatabaseCommon.PathToTile(path, type, tile, compressed);

            if (!FileSystemFacade.FileSystem.Exists(location))
            {
                return null;
            }

            if (compressed)
            {
                return CreateInflateStream(FileSystemFacade.FileSystem.OpenRead(location));
            }

            return FileSystemFacade.FileSystem.OpenRead(location);
        }
        
        /// <summary>
        /// Creates a tile.
        /// </summary>
        public static Stream CreateTile(string path, OsmGeoType type, Tile tile, bool compressed = false)
        {
            var location = DatabaseCommon.PathToTile(path, type, tile, compressed);

            var fileDirectory = FileSystemFacade.FileSystem.DirectoryForFile(location);
            if (!FileSystemFacade.FileSystem.DirectoryExists(fileDirectory))
            {
                FileSystemFacade.FileSystem.CreateDirectory(fileDirectory);
            }

            if (compressed)
            {
                return CreateDeflateStream(FileSystemFacade.FileSystem.Open(location, FileMode.Create));
            }

            return FileSystemFacade.FileSystem.Open(location, FileMode.Create);
        }

        /// <summary>
        /// Builds a path to the database meta file.
        /// </summary>
        public static string PathToMeta(string path)
        {
            return FileSystemFacade.FileSystem.Combine(path, "meta.bin");
        }

        /// <summary>
        /// Builds a path to the given tile.
        /// </summary>
        public static string PathToTile(string path, OsmGeoType type, Tile tile, bool compressed = false)
        {
            var location = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToInvariantString(),
                tile.X.ToInvariantString());
            if (type == OsmGeoType.Node)
            {
                location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".nodes.osm.bin");
            }
            else if (type == OsmGeoType.Way)
            {
                location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".ways.osm.bin");
            }
            else
            {
                location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".relations.osm.bin");
            }

            if (compressed)
            {
                return location + ".zip";
            }
            return location;
        }
        
        /// <summary>
        /// Creates a local object.
        /// </summary>
        public static Stream CreateLocalTileObject(string path, Tile tile, OsmGeo osmGeo, bool compressed = false)
        {
            var location = DatabaseCommon.BuildPathToLocalTileObject(path, tile, osmGeo, compressed);

            var fileDirectory = FileSystemFacade.FileSystem.DirectoryForFile(location);
            if (!FileSystemFacade.FileSystem.DirectoryExists(fileDirectory))
            {
                FileSystemFacade.FileSystem.CreateDirectory(fileDirectory);
            }

            if (compressed)
            {
                return CreateDeflateStream(FileSystemFacade.FileSystem.Open(location, FileMode.Create));
            }

            return FileSystemFacade.FileSystem.Open(location, FileMode.Create);
        }

        /// <summary>
        /// Builds a path to a local object in the given tile.
        /// </summary>
        public static string BuildPathToLocalTileObject(string path, Tile tile, OsmGeo osmGeo, bool compressed = false)
        {
            var location = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToInvariantString(),
                tile.X.ToInvariantString(), tile.Y.ToInvariantString());
            
            switch (osmGeo.Type)
            {
                case OsmGeoType.Node:
                    location = FileSystemFacade.FileSystem.Combine(location, $"{osmGeo.Id.Value}.node.osm.bin");
                    break;
                case OsmGeoType.Way:
                    location = FileSystemFacade.FileSystem.Combine(location, $"{osmGeo.Id.Value}.way.osm.bin");
                    break;
                default:
                    location = FileSystemFacade.FileSystem.Combine(location, $"{osmGeo.Id.Value}.relation.osm.bin");
                    break;
            }

            if (compressed)
            {
                return location + ".zip";
            }
            return location;
        }
        
        /// <summary>
        /// Creates a tile.
        /// </summary>
        public static string PathToIndex(string path, OsmGeoType type, Tile tile)
        {
            var location = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToInvariantString(),
                tile.X.ToInvariantString());
            switch (type)
            {
                case OsmGeoType.Node:
                    location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".nodes.idx");
                    break;
                case OsmGeoType.Way:
                    location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".ways.idx");
                    break;
                default:
                    location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".relations.idx");
                    break;
            }
            return location;
        }
        
        /// <summary>
        /// Loads an index for the given tile from disk (if any).
        /// </summary>
        public static Index LoadIndex(string path, Tile tile, OsmGeoType type, bool mapped = false)
        {
            var extension = ".nodes.idx";
            switch (type)
            {
                case OsmGeoType.Way:
                    extension = ".ways.idx";
                    break;
                case OsmGeoType.Relation:
                    extension = ".relations.idx";
                    break;
            }

            var location = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToInvariantString(),
                tile.X.ToInvariantString(), tile.Y.ToInvariantString() + extension);
            if (!FileSystemFacade.FileSystem.Exists(location))
            {
                return null;
            }

            if (mapped)
            {
                var stream = FileSystemFacade.FileSystem.OpenRead(location);
                return Index.Deserialize(stream, ArrayProfile.NoCache);
            }
            using (var stream = FileSystemFacade.FileSystem.OpenRead(location))
            {
                return Index.Deserialize(stream);
            }
        }
        
        /// <summary>
        /// Saves an index for the given tile to disk.
        /// </summary>
        public static void SaveIndex(string path, Tile tile, OsmGeoType type, Index index)
        {
            var extension = ".nodes.idx";
            switch (type)
            {
                case OsmGeoType.Way:
                    extension = ".ways.idx";
                    break;
                case OsmGeoType.Relation:
                    extension = ".relations.idx";
                    break;
            }

            var location = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToInvariantString(),
                tile.X.ToInvariantString(), tile.Y.ToInvariantString() + extension);
            var parentPath = FileSystemFacade.FileSystem.ParentDirectory(location);
            if (!FileSystemFacade.FileSystem.DirectoryExists(parentPath))
            {
                FileSystemFacade.FileSystem.CreateDirectory(parentPath);
            }
            using (var stream = FileSystemFacade.FileSystem.Open(location, FileMode.Create))
            {
                index.Serialize(stream);
            }
        }
        
        /// <summary>
        /// Loads a deleted index for the given tile from disk (if any).
        /// </summary>
        internal static DeletedIndex LoadDeletedIndex(string path, Tile tile, OsmGeoType type, bool mapped = false)
        {
            var location = DatabaseCommon.PathToDeletedIndex(path, tile, type);
            if (!FileSystemFacade.FileSystem.Exists(location))
            {
                return null;
            }

            if (mapped)
            {
                var stream = FileSystemFacade.FileSystem.OpenRead(location);
                return DeletedIndex.Deserialize(stream, ArrayProfile.NoCache);
            }
            using (var stream = FileSystemFacade.FileSystem.OpenRead(location))
            {
                return DeletedIndex.Deserialize(stream);
            }
        }

        /// <summary>
        /// Saves a deleted index for the given tile to disk.
        /// </summary>
        internal static void SaveDeletedIndex(string path, Tile tile, OsmGeoType type, DeletedIndex deletedIndex)
        {
            var location = DatabaseCommon.PathToDeletedIndex(path, tile, type);
            var parentPath = FileSystemFacade.FileSystem.ParentDirectory(location);
            if (!FileSystemFacade.FileSystem.DirectoryExists(parentPath))
            {
                FileSystemFacade.FileSystem.CreateDirectory(parentPath);
            }

            using (var stream = FileSystemFacade.FileSystem.Open(location, FileMode.Create))
            {
                deletedIndex.Serialize(stream);
            }
        }

        /// <summary>
        /// Builds a path to a deleted index.
        /// </summary>
        public static string PathToDeletedIndex(string path, Tile tile, OsmGeoType type)
        {
            var location = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToInvariantString(),
                tile.X.ToInvariantString());
            switch (type)
            {
                case OsmGeoType.Node:
                    location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".nodes.idx.deleted");
                    break;
                case OsmGeoType.Way:
                    location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".ways.idx.deleted");
                    break;
                default:
                    location = FileSystemFacade.FileSystem.Combine(location, tile.Y.ToInvariantString() + ".relations.idx.deleted");
                    break;
            }
            return location;
        }
        
//        /// <summary>
//        /// Opens a stream to append to a deleted index. Creates the index if it doesn't exist yet.
//        /// </summary>
//        /// <param name="path">The path.</param>
//        /// <param name="type">The object type.</param>
//        /// <param name="tile">The tile.</param>
//        /// <returns>The stream.</returns>
//        internal static Stream OpenAppendStreamDeletedIndex(string path, OsmGeoType type, Tile tile)
//        {
//            var indexPath = DatabaseCommon.PathToDeletedIndex(path, type, tile);
//
//            var parentPath = FileSystemFacade.FileSystem.ParentDirectory(indexPath);
//            if (!FileSystemFacade.FileSystem.DirectoryExists(parentPath))
//            {
//                FileSystemFacade.FileSystem.CreateDirectory(parentPath);
//            }
//            
//            return FileSystemFacade.FileSystem.Open(indexPath, FileMode.Append);
//        }
        
//        /// <summary>
//        /// Opens a stream to append to an index. Creates the index if it doesn't exist yet.
//        /// </summary>
//        /// <param name="path">The path.</param>
//        /// <param name="type">The object type.</param>
//        /// <param name="tile">The tile.</param>
//        /// <returns>The stream.</returns>
//        internal static Stream OpenAppendStreamIndex(string path, OsmGeoType type, Tile tile)
//        {
//            var indexPath = DatabaseCommon.PathToIndex(path, type, tile);
//            if (!FileSystemFacade.FileSystem.Exists(indexPath))
//            {
//                return FileSystemFacade.FileSystem.Open(indexPath, FileMode.Create);
//            }
//
//            var stream = FileSystemFacade.FileSystem.OpenWrite(indexPath);
//            stream.Seek(stream.Length, SeekOrigin.End);
//            return stream;
//        }

        /// <summary>
        /// Opens a stream to append to a data tile. Creates the tile if it doesn't exist yet.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="type">The object type.</param>
        /// <param name="tile">The tile.</param>
        /// <returns>The stream.</returns>
        internal static Stream OpenAppendStreamTile(string path, OsmGeoType type, Tile tile)
        {
            var location = DatabaseCommon.PathToTile(path, type, tile, false);
            var parentPath = FileSystemFacade.FileSystem.ParentDirectory(location);
            if (!FileSystemFacade.FileSystem.DirectoryExists(parentPath))
            {
                FileSystemFacade.FileSystem.CreateDirectory(parentPath);
            }
            return FileSystemFacade.FileSystem.Open(location, FileMode.Append);
        }
    }
}