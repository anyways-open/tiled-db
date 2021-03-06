using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using OsmSharp.Db.Tiled.IO;
using OsmSharp.Db.Tiled.OsmTiled;
using OsmSharp.Db.Tiled.OsmTiled.IO;
using OsmSharp.Db.Tiled.Tests.Mocks;
using OsmSharp.Streams;

namespace OsmSharp.Db.Tiled.Tests.Build
{
    /// <summary>
    /// Contains builder tests.
    /// </summary>
    [TestFixture]
    public class OsmTiledHistoryDbBuilderTests
    {
        /// <summary>
        /// Tests building a database.
        /// </summary>
        [Test]
        public void OsmDbBuilder_BuildDb_ShouldBuildInitial()
        {
            var root = $"/{Guid.NewGuid().ToString()}";
            
            FileSystemFacade.GetFileSystem = MockFileSystem.GetMockFileSystem;
            FileSystemFacade.FileSystem.CreateDirectory($"{root}/data");

            // build the database.
            var osmGeos = new OsmGeo[]
            {
                new Node()
                {
                    Id = 0,
                    Latitude = 50,
                    Longitude = 4,
                    ChangeSetId = 1,
                    UserId = 1,
                    UserName = "Ben",
                    Visible = true,
                    TimeStamp = DateTime.Now,
                    Version = 1
                },
                new Node()
                {
                    Id = 1,
                    Latitude = 50,
                    Longitude = 4,
                    ChangeSetId = 1,
                    UserId = 1,
                    UserName = "Ben",
                    Visible = true,
                    TimeStamp = DateTime.Now,
                    Version = 1
                },
                new Way()
                {
                    Id = 0,
                    ChangeSetId = 1,
                    Nodes = new long[]
                    {
                        0, 1
                    },
                    Tags = null,
                    TimeStamp = DateTime.Now,
                    UserId = 1,
                    UserName = "Ben",
                    Version = 1,
                    Visible = true
                }
            };
            var db = OsmSharp.Db.Tiled.Build.OsmTiledHistoryDbBuilder.Build(
               osmGeos, $"{root}/data");

            Assert.True(FileSystemFacade.FileSystem.DirectoryExists($"{root}/data"));

            var initialPath = OsmTiledDbOperations.BuildDbPath($"{root}/data", db.Latest.Id, null, OsmTiledDbType.Full);
            
            // check if initial dir exists.
            Assert.True(FileSystemFacade.FileSystem.Exists(
                FileSystemFacade.FileSystem.Combine(initialPath, "meta.json")));
        }
    }
}