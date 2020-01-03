using System;
using System.IO;
using OsmSharp.Db.Tiled.IO;
using OsmSharp.Db.Tiled.Snapshots.Build;
using OsmSharp.Streams;

namespace OsmSharp.Db.Tiled.Build
{
    /// <summary>
    /// Contains code to build an OSM db.
    /// </summary>
    public static class OsmDbBuilder
    {
        private const string InitialSnapshotDbPath = "initial";

        /// <summary>
        /// Builds a new database and write the structure to the given path.
        /// </summary>
        public static OsmDb BuildDb(this OsmStreamSource source, string path, uint maxZoom = 12)
        {
            if (maxZoom % 2 != 0) throw new ArgumentException($"{nameof(maxZoom)} max zoom has to be a multiple of 2."); 
            
            if (!FileSystemFacade.FileSystem.DirectoryExists(path)) throw new DirectoryNotFoundException(
                $"Cannot create OSM db: {path} not found.");
            
            var snapshotDbPath = FileSystemFacade.FileSystem.Combine(path, InitialSnapshotDbPath);
            if (!FileSystemFacade.FileSystem.DirectoryExists(snapshotDbPath)) FileSystemFacade.FileSystem.CreateDirectory(snapshotDbPath);

            // build the snapshot db.
            source.Build(snapshotDbPath, maxZoom);
            
            // generate and write the path.
            var osmDbMeta = new OsmDbMeta()
            {
                Latest = snapshotDbPath
            };
            OsmDbOperations.SaveDbMeta(path, osmDbMeta);
            
            // return the osm db.
            return new OsmDb(path);
        }
    }
}