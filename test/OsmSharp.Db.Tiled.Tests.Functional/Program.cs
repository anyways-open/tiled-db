﻿using System;
using System.IO;
using Serilog;
using System.Collections.Generic;
using OsmSharp.Changesets;
using OsmSharp.Db.Tiled.Ids;
using OsmSharp.Logging;
using OsmSharp.Streams;

namespace OsmSharp.Db.Tiled.Tests.Functional
{
    class Program
    {
        static void Main(string[] args)
        {
//#if DEBUG
            if (args == null || args.Length == 0)
            {
                args = new string[]
                {
                    @"/data/work/data/OSM/wechel.osm.pbf",
                    @"/data/work/anyways/data/test/tilesdb/",
                    @"14"
                };
            }
//#endif
            
            // enable logging.
            OsmSharp.Logging.Logger.LogAction = (origin, level, message, parameters) =>
            {
                var formattedMessage = $"{origin} - {message}";
                switch (level)
                {
                    case "critical":
                        Log.Fatal(formattedMessage);
                        break;
                    case "error":
                        Log.Error(formattedMessage);
                        break;
                    case "warning":
                        Log.Warning(formattedMessage);
                        break; 
                    case "verbose":
                        Log.Verbose(formattedMessage);
                        break; 
                    case "information":
                        Log.Information(formattedMessage);
                        break; 
                    default:
                        Log.Debug(formattedMessage);
                        break;
                }
            };

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine("logs", "log-.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                // validate arguments.
                if (args.Length < 3)
                {
                    Log.Fatal("Expected 4 arguments: inputfile db zoom");
                    return;
                }
                if (!File.Exists(args[0]))
                {
                    Log.Fatal("Input file not found: {0}", args[0]);
                    return;
                }
                if (!Directory.Exists(args[1]))
                {
                    Log.Fatal("Db directory doesn't exist: {0}", args[1]);
                    return;
                }
                if (!uint.TryParse(args[2], out var zoom))
                {
                    Log.Fatal("Can't parse zoom: {0}", args[2]);
                    return;
                }

                var ticks = DateTime.Now.Ticks;
                if (!File.Exists(Path.Combine(args[1], "0", "0", "0.nodes.idx")))
                {
                    Log.Information("The tiled DB doesn't exist yet, rebuilding...");
                    var source = new OsmSharp.Streams.PBFOsmStreamSource(
                        File.OpenRead(args[0]));
                    var progress = new OsmSharp.Streams.Filters.OsmStreamFilterProgress();
                    progress.RegisterSource(source);

                    // splitting tiles and writing indexes.
                    Build.Builder.Build(progress, args[1], zoom);
                }
                else
                {
                    Log.Information("The tiled DB already exists, reusing...");
                }
                
                // create a database object that can read individual objects.
                Log.Information($"Loading database: {args[1]}");
                var db = new DatabaseSnapshot(args[1], new DatabaseMeta()
                {
                    Base = null,
                    Zoom = zoom
                });

                var testOutput = "test-output";
                foreach (var baseTile in db.GetTiles())
                {
                    Log.Information($"Base tile found: {baseTile}");

                    var file = Path.Combine(testOutput, baseTile.Zoom.ToString(), baseTile.X.ToString(),
                        baseTile.Y.ToString(), "index.json");
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                    {
                        fileInfo.Directory.Create();
                    }

                    using (var stream = File.Open(file, FileMode.Create))
                    {
                        var target = new OsmSharp.Streams.BinaryOsmStreamTarget(stream);
                        target.Initialize();

                        target.RegisterSource(db.GetTile(baseTile));

                        target.Pull();
                        target.Close();
                    }
                }
                var span = new TimeSpan(DateTime.Now.Ticks - ticks);
                Log.Information($"Writing tiles took: {span}");

                var diff1 = db.ApplyChangeset(new OsmChange()
                {
                    Delete = new OsmGeo[]
                    {
                        new Way()
                        {
                            Id = 76586523
                        }
                    }
                });
            }
            catch (Exception e)
            {
                Log.Fatal(e, "Unhandled exception.");
                throw;
            }
        }
    }
}