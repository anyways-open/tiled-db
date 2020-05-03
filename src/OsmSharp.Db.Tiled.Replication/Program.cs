﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OsmSharp.Changesets;
using OsmSharp.Logging;
using OsmSharp.Replication;
using OsmSharp.Streams;
using Serilog;

namespace OsmSharp.Db.Tiled.Replication
{
    internal static class Program
    {
        static async Task Main(string[] args)
        {
            // enable logging.
            Logger.LogAction = (origin, level, message, parameters) =>
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
            OsmSharp.Db.Tiled.Logging.Log.LogAction = (type, message) =>
            {
                switch (type)
                {
                    case OsmSharp.Db.Tiled.Logging.TraceEventType.Critical:
                        Log.Fatal(message);
                        break;
                    case OsmSharp.Db.Tiled.Logging.TraceEventType.Error:
                        Log.Error(message);
                        break;
                    case OsmSharp.Db.Tiled.Logging.TraceEventType.Warning:
                        Log.Warning(message);
                        break;
                    case OsmSharp.Db.Tiled.Logging.TraceEventType.Verbose:
                        Log.Verbose(message);
                        break;
                    case OsmSharp.Db.Tiled.Logging.TraceEventType.Information:
                        Log.Information(message);
                        break;
                    default:
                        Log.Debug(message);
                        break;
                }
            };

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine("logs", "log-.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");

            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            var planetFile = config["planet"];
            var dbPath = config["db"];

            var lockFile = new FileInfo(Path.Combine(dbPath, "replication.lock"));
            if (LockHelper.IsLocked(lockFile.FullName))
            {
                return;
            }

            try
            {
                LockHelper.WriteLock(lockFile.FullName);

                // try loading the db, if it doesn't exist build it.
                var ticks = DateTime.Now.Ticks;
                if (!OsmTiledHistoryDb.TryLoad(dbPath, out var db))
                {
                    Log.Information("The DB doesn't exist yet, building...");
                    
                    var source = new PBFOsmStreamSource(
                        File.OpenRead(planetFile));
                    var progress = new OsmSharp.Streams.Filters.OsmStreamFilterProgress();
                    progress.RegisterSource(source);
                    
                    // splitting tiles and writing indexes.
                    db = OsmTiledHistoryDb.Create(dbPath, progress);
                    Log.Information("DB built successfully.");
                    Log.Information($"Took {new TimeSpan(DateTime.Now.Ticks - ticks).TotalSeconds}s");
                    return;
                }
                
                if (db == null) throw new Exception("Db loading failed!");
                Log.Information("DB loaded successfully.");

                // keep going until caught up.
                while (true)
                {
                    ticks = DateTime.Now.Ticks;
                    // collect minutely diffs.
                    var minuteEnumerator =
                        await ReplicationConfig.Minutely.GetDiffEnumerator(
                            db.Latest.EndTimestamp.AddSeconds(1));
                    if (minuteEnumerator == null)
                    {
                        Log.Information("No new changes.");
                        return;
                    }

                    var changeSets = new List<OsmChange>();
                    var timestamp = DateTime.MinValue;
                    while (await minuteEnumerator.MoveNext())
                    {
                        Log.Verbose($"Downloading diff: {minuteEnumerator.State}");
                        changeSets.Add(await minuteEnumerator.Diff());
                        if (timestamp < minuteEnumerator.State.EndTimestamp)
                            timestamp = minuteEnumerator.State.EndTimestamp;
                        
                        if (timestamp.Day != db.Latest.EndTimestamp.Day) break;
                        if (changeSets.Count >= 10) break;
                    }
                    var dayCrossed = (timestamp.Day != db.Latest.EndTimestamp.Day);

                    // apply changes.
                    if (changeSets.Count == 0)
                    {
                        Log.Information("No new changes.");
                        return;
                    }

                    // squash changes.
                    var changeSet = changeSets[0];
                    if (changeSets.Count > 1)
                    {
                        Log.Verbose($"Squashing changes...");
                        changeSet = changeSets.Squash();
                    }

                    // apply diff.
                    Log.Information($"Applying changes...");
                    db.ApplyDiff(changeSet, timestamp);
                    Log.Information($"Took {new TimeSpan(DateTime.Now.Ticks - ticks).TotalSeconds}s");
                    
                    // take snapshot at each hour crossing.
                    if (dayCrossed)
                    {
                        ticks = DateTime.Now.Ticks;
                        Log.Information($"Day crossing, taking snapshot...");
                        db.TakeSnapshot(timeSpan: new TimeSpan(1, 0, 0));
                        Log.Information($"Took {new TimeSpan(DateTime.Now.Ticks - ticks).TotalSeconds}s");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Fatal(e, "Unhandled exception during processing.");
            }
            finally
            {
                File.Delete(lockFile.FullName);
            }
        }
    }
}