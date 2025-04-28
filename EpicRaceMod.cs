using GTA;
using GTA.Math;
using GTA.Native;
using NativeUI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

public class EpicRaceMod : Script
{
    private static readonly Random Random = new Random();
    private readonly MenuPool menuPool;
    private readonly UIMenu mainMenu;
    private readonly UIMenu setupMenu;
    private readonly RaceManager raceManager;

    public EpicRaceMod()
    {
        raceManager = new RaceManager(this);
        menuPool = new MenuPool();
        mainMenu = new UIMenu("Epic Race Mod", "Coop Race with Friends");
        menuPool.Add(mainMenu);
        setupMenu = new UIMenu("Race Setup", "Configure Race");
        menuPool.Add(setupMenu);
        SetupMenuItems();
        Tick += (s, e) =>
        {
            raceManager.Update();
            menuPool.ProcessMenus();
        };
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5)
                mainMenu.Visible = !mainMenu.Visible;
        };
    }

    private void SetupMenuItems()
    {
        var startRace = new UIMenuItem("Start Race");
        mainMenu.AddItem(startRace);
        startRace.Activated += (s, i) =>
        {
            if (raceManager.HasValidTrack())
            {
                raceManager.StartRace();
                mainMenu.Visible = false;
            }
            else
                UI.Notify("Add at least 2 checkpoints first!");
        };

        var setupRace = new UIMenuItem("Configure Race");
        mainMenu.AddItem(setupRace);
        mainMenu.BindMenuToItem(setupMenu, setupRace);

        var addCheckpoint = new UIMenuItem("Add Checkpoint (Current Position)");
        setupMenu.AddItem(addCheckpoint);
        addCheckpoint.Activated += (s, i) =>
        {
            raceManager.AddCheckpoint(Game.Player.Character.Position);
            UI.Notify($"Checkpoint {raceManager.CheckpointCount} added!");
        };

        var vehicles = new List<dynamic> { "Bati", "Sanchez", "Hexer" };
        var vehicleItem = new UIMenuListItem("Bike Model", vehicles, 0);
        setupMenu.AddItem(vehicleItem);
        setupMenu.OnListChange += (s, item, index) =>
        {
            if (item == vehicleItem)
                raceManager.SetVehicleModel(vehicles[index]);
        };

        var tracks = new List<dynamic> { "Los Santos Loop", "Blaine County Sprint" };
        var trackItem = new UIMenuListItem("Track", tracks, 0);
        setupMenu.AddItem(trackItem);
        setupMenu.OnListChange += (s, item, index) =>
        {
            raceManager.LoadTrack(index);
        };

        var placeRamp = new UIMenuItem("Place Ramp");
        setupMenu.AddItem(placeRamp);
        placeRamp.Activated += (s, i) =>
        {
            raceManager.PlaceObject("prop_mp_ramp_03");
            UI.Notify("Ramp placed!");
        };
    }

    private class RaceManager
    {
        private readonly EpicRaceMod script;
        private readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
        private readonly List<PlayerHandler> racers = new List<PlayerHandler>();
        private readonly List<Obstacle> obstacles = new List<Obstacle>();
        private readonly List<PowerUp> powerUps = new List<PowerUp>();
        private readonly List<Prop> placedObjects = new List<Prop>();
        private bool raceStarted = false;
        private int currentLap = 1;
        private const int TotalLaps = 3;
        private string vehicleModel = "bati";
        private readonly List<List<Vector3>> predefinedTracks = new List<List<Vector3>>
        {
            new List<Vector3> { new Vector3(-425.67f, 1126.76f, 325.85f), new Vector3(-350.23f, 1150.45f, 325.85f), new Vector3(-300.89f, 1100.12f, 325.85f), new Vector3(-425.67f, 1126.76f, 325.85f) },
            new List<Vector3> { new Vector3(-1000.45f, 2000.78f, 50.23f), new Vector3(-900.12f, 2100.56f, 50.23f), new Vector3(-800.34f, 2000.89f, 50.23f), new Vector3(-1000.45f, 2000.78f, 50.23f) }
        };

        public int CheckpointCount => checkpoints.Count;

        public bool HasValidTrack() => checkpoints.Count >= 2;

        public IReadOnlyList<Checkpoint> Checkpoints => checkpoints.AsReadOnly();

        public RaceManager(EpicRaceMod scriptInstance)
        {
            script = scriptInstance;
        }

        public void AddCheckpoint(Vector3 pos)
        {
            checkpoints.Add(new Checkpoint(pos, checkpoints.Count == 0));
        }

        public void SetVehicleModel(string model)
        {
            vehicleModel = model.ToLower();
        }

        public void LoadTrack(int index)
        {
            foreach (var cp in checkpoints) cp.Remove();
            checkpoints.Clear();
            foreach (var pos in predefinedTracks[index])
                checkpoints.Add(new Checkpoint(pos, checkpoints.Count == 0));
            UI.Notify($"Loaded track: {index + 1}");
        }

        public void PlaceObject(string model)
        {
            var pos = Game.Player.Character.Position + Game.Player.Character.ForwardVector * 5f;
            var prop = World.CreateProp(model, pos, false, false);
            prop.FreezePosition = false;
            prop.HasCollision = true;
            placedObjects.Add(prop);
        }

        public void StartRace()
        {
            if (raceStarted || !HasValidTrack()) return;
            raceStarted = true;

            foreach (var r in racers) r.Remove();
            racers.Clear();
            foreach (var p in powerUps) p.Remove();
            powerUps.Clear();

            racers.Add(new PlayerHandler(script, Game.Player.Character, vehicleModel));
            for (int i = 0; i < 4; i++)
                racers.Add(new AIPlayerHandler(script, World.CreateRandomPed(checkpoints[0].Position), vehicleModel));

            obstacles.Add(new Obstacle("prop_barrel_02a", checkpoints[1].Position + new Vector3(0, 5, 0), true));
            powerUps.Add(new PowerUp(checkpoints[2].Position + new Vector3(0, 0, 1)));

            for (int i = 3; i > 0; i--)
            {
                UI.ShowSubtitle(i.ToString(), 1000);
                Script.Wait(1000);
            }
            UI.ShowSubtitle("GO!", 1000);

            Function.Call(Hash.SET_WEATHER_TYPE_NOW, "CLEAR");
            BroadcastRaceStart();
        }

        public void Update()
        {
            if (!raceStarted) return;

            foreach (var racer in racers) racer.Update();
            foreach (var obstacle in obstacles) obstacle.Update();
            foreach (var powerUp in powerUps) powerUp.Update(racers);

            UI.ShowSubtitle($"Lap: {currentLap}/{TotalLaps} | Racers: {racers.Count}", 100);
            CheckRaceProgress();
        }

        private void CheckRaceProgress()
        {
            foreach (var racer in racers.ToArray())
            {
                if (racer.HasFinishedLap() && racer.LapCount < TotalLaps)
                {
                    racer.LapCount++;
                    racer.ResetCheckpoint();
                    if (racer.LapCount == TotalLaps)
                    {
                        UI.Notify($"{(racer.IsPlayer ? "You" : "A racer")} finished!");
                        EndRace();
                    }
                }
            }

            if (currentLap < TotalLaps && racers.TrueForAll(r => r.HasFinishedLap()))
            {
                currentLap++;
                EliminateLastRacer();
            }
        }

        private void EliminateLastRacer()
        {
            if (racers.Count <= 1) return;
            var lastRacer = racers.OrderBy(r => r.CheckpointIndex).First();
            racers.Remove(lastRacer);
            lastRacer.Remove();
            UI.Notify("Last racer eliminated!");
        }

        private void EndRace()
        {
            raceStarted = false;
            foreach (var r in racers) r.Remove();
            foreach (var cp in checkpoints) cp.Remove();
            foreach (var p in powerUps) p.Remove();
            foreach (var o in obstacles) o.Remove();
            foreach (var p in placedObjects) p.Delete();
            checkpoints.Clear();
            racers.Clear();
            powerUps.Clear();
            obstacles.Clear();
            placedObjects.Clear();
            Function.Call(Hash.SET_WEATHER_TYPE_NOW, "CLEAR");
            UI.Notify("Race ended!");
        }

        private void BroadcastRaceStart() { /* Stub for multiplayer */ }
    }

    private class Checkpoint
    {
        public Vector3 Position { get; }
        public Blip Blip { get; }
        public Prop Ring { get; }

        public Checkpoint(Vector3 pos, bool isStart)
        {
            Position = pos;
            Blip = World.CreateBlip(pos);
            Blip.Sprite = isStart ? BlipSprite.Race : BlipSprite.Standard;
            Blip.Color = isStart ? BlipColor.Green : BlipColor.Yellow;
            Ring = World.CreateProp("prop_checkpoint_02b", pos, false, false);
        }

        public void Remove()
        {
            Blip.Remove();
            Ring.Delete();
        }
    }

    private class PlayerHandler
    {
        protected readonly EpicRaceMod script;
        public Ped Ped { get; protected set; }
        public Vehicle Bike { get; protected set; }
        protected int checkpointIndex = 0;
        public int LapCount { get; set; } = 1;
        public bool IsPlayer => Ped == Game.Player.Character;
        private readonly string vehicleModel;

        public PlayerHandler(EpicRaceMod scriptInstance, Ped p, string model)
        {
            script = scriptInstance;
            Ped = p;
            vehicleModel = model.ToLower();
        }

        public virtual void PrepareForRace()
        {
            Bike = World.CreateVehicle(vehicleModel, script.raceManager.Checkpoints[0].Position);
            Bike.FreezePosition = false;
            Bike.HasCollision = true;
            Bike.PrimaryColor = (VehicleColor)Random.Next(0, 160);
            Ped.SetIntoVehicle(Bike, VehicleSeat.Driver);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 37, true); // Disable weapons
        }

        public virtual void Update()
        {
            if (Ped.IsDead || !Ped.IsInVehicle(Bike) || Ped.Position.Z < -10)
                Respawn();

            if (IsAtCheckpoint())
                checkpointIndex++;

            CheckForCheating();
        }

        protected bool IsAtCheckpoint()
        {
            if (checkpointIndex >= script.raceManager.Checkpoints.Count) return false;
            return Ped.Position.DistanceTo(script.raceManager.Checkpoints[checkpointIndex].Position) < 5f;
        }

        public bool HasFinishedLap() => checkpointIndex >= script.raceManager.Checkpoints.Count;

        public void ResetCheckpoint() => checkpointIndex = 0;

        protected void Respawn()
        {
            int lastIndex = checkpointIndex > 0 ? checkpointIndex - 1 : 0;
            Ped.Position = script.raceManager.Checkpoints[lastIndex].Position;
            Bike.Position = Ped.Position;
            Bike.FreezePosition = false;
            Bike.HasCollision = true;
            Ped.SetIntoVehicle(Bike, VehicleSeat.Driver);
            Script.Wait(2000);
            UI.Notify("Respawned at last checkpoint!");
        }

        protected void CheckForCheating()
        {
            if (checkpointIndex >= script.raceManager.Checkpoints.Count) return;
            var expectedPos = script.raceManager.Checkpoints[checkpointIndex].Position;
            if (Ped.Position.DistanceTo(expectedPos) > 50f || Bike.Speed > 60f)
            {
                UI.Notify("Cheating detected!");
                Respawn();
            }
        }

        public void Remove()
        {
            Bike.Delete();
            if (!IsPlayer) Ped.Delete();
        }

        public int CheckpointIndex => checkpointIndex;
    }

    private class AIPlayerHandler : PlayerHandler
    {
        public AIPlayerHandler(EpicRaceMod scriptInstance, Ped p, string model) : base(scriptInstance, p, model) { }

        public override void PrepareForRace()
        {
            base.PrepareForRace();
            DriveToNextCheckpoint();
        }

        public override void Update()
        {
            base.Update();
            if (IsAtCheckpoint())
            {
                checkpointIndex++;
                DriveToNextCheckpoint();
            }
        }

        private void DriveToNextCheckpoint()
        {
            if (checkpointIndex < script.raceManager.Checkpoints.Count)
            {
                var target = script.raceManager.Checkpoints[checkpointIndex].Position;
                Ped.Task.DriveTo(Bike, target, 10f, 50f, (int)DrivingStyle.AvoidTraffic);
            }
        }
    }

    private class Obstacle
    {
        private readonly Prop prop;
        private readonly Vector3 startPos;
        private readonly bool isMoving;

        public Obstacle(string model, Vector3 pos, bool moving = false)
        {
            prop = World.CreateProp(model, pos, false, false);
            prop.FreezePosition = false;
            prop.HasCollision = true;
            startPos = pos;
            isMoving = moving;
        }

        public void Update()
        {
            if (isMoving)
            {
                float offset = (float)Math.Sin(Game.GameTime / 1000f) * 10f;
                prop.Position = new Vector3(startPos.X + offset, startPos.Y, startPos.Z);
            }
        }

        public void Remove() => prop.Delete();
    }

    private class PowerUp
    {
        private readonly Vector3 position;
        private readonly Blip blip;
        private bool active = true;

        public PowerUp(Vector3 pos)
        {
            position = pos;
            blip = World.CreateBlip(pos);
            blip.Sprite = BlipSprite.Standard;
            blip.Color = BlipColor.Green;
        }

        public void Update(List<PlayerHandler> racers)
        {
            if (!active) return;
            foreach (var racer in racers)
            {
                if (racer.Ped.Position.DistanceTo(position) < 3f)
                {
                    racer.Bike.Speed += 10f;
                    UI.Notify("Speed boost collected!");
                    active = false;
                    blip.Remove();
                    break;
                }
            }
        }

        public void Remove() => blip.Remove();
    }
}