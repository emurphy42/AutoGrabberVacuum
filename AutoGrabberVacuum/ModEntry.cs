using GenericModConfigMenu;
using Microsoft.Xna.Framework.Media;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Buildings;
using StardewValley.Objects;

namespace AutoGrabberVacuum
{
    public class ModEntry : Mod
    {
        /*********
        ** Properties
        *********/
        /// <summary>The mod configuration from the player.</summary>
        private ModConfig Config;

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            this.Config = this.Helper.ReadConfig<ModConfig>();

            Helper.Events.GameLoop.GameLaunched += (e, a) => OnGameLaunched(e, a);
            Helper.Events.GameLoop.DayStarted += (e, a) => OnDayStarted(e, a);
            Helper.Events.Input.ButtonPressed += (e, a) => OnButtonPressed(e, a);
        }

        /// <summary>Add to Generic Mod Config Menu</summary>
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // get Generic Mod Config Menu's API (if it's installed)
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            // register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            // add config options
            configMenu.AddKeybind(
                mod: this.ModManifest,
                name: () => Helper.Translation.Get("Options_VacuumKey"),
                getValue: () => this.Config.VacuumKey,
                setValue: value => this.Config.VacuumKey = value
            );
        }

        /// <summary>Move items into auto-grabbers at start of day</summary>
        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            MoveItemsIntoAutoGrabbers(manualRequest: false);
        }

        /// <summary>Move items into auto-grabbers on request</summary>
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button == this.Config.VacuumKey)
            {
                MoveItemsIntoAutoGrabbers(manualRequest: true);
            }
        }

        /// <summary>Move items into auto-grabbers</summary>
        private void MoveItemsIntoAutoGrabbers(bool manualRequest)
        {
            var numberItemsMoved = 0;

            // Check each building on the farm

            foreach (var building in Game1.getFarm().buildings)
            {
                this.Monitor.Log($"[Auto-Grabber Vacuum] Checking {building.buildingType} {building.id}", LogLevel.Trace);

                // Does it have an interior?

                if (building.indoors == null || building.indoors.Value == null)
                {
                    this.Monitor.Log("[Auto-Grabber Vacuum] No interior", LogLevel.Trace);
                    continue;
                }

                // Do any animals live in the building?

                var allAnimals = building.indoors.Value.animals;
                if (allAnimals == null || allAnimals.Count() == 0)
                {
                    this.Monitor.Log("[Auto-Grabber Vacuum] No animals", LogLevel.Trace);
                    continue;
                }

                // Does the building contain a (valid) Auto-Grabber?

                Chest? autoGrabberInterior = null;
                Utility.ForEachItemIn(building.indoors.Value, (Func<Item, bool>)(item => {
                    this.Monitor.Log($"[Auto-Grabber Vacuum] Checking item ID {item.QualifiedItemId} = {item.Name}", LogLevel.Trace);
                    var currentObject = (StardewValley.Object)item;
                    if (currentObject.QualifiedItemId != "(BC)165")
                    {
                        return true;
                    }
                    if (currentObject.heldObject.Value is Chest chest) {
                        this.Monitor.Log("[Auto-Grabber Vacuum] Has Auto-Grabber", LogLevel.Trace);
                        autoGrabberInterior = chest;
                        return false; // skip rest of items in this building
                    }
                    this.Monitor.Log($"[Auto-Grabber Vacuum] Building {building.GetIndoorsName()} has Auto-Grabber, but its interior is missing", LogLevel.Error);
                    return true;
                }));
                if (autoGrabberInterior == null)
                {
                    this.Monitor.Log("[Auto-Grabber Vacuum] No Auto-Grabber", LogLevel.Trace);
                    continue;
                }

                // What items can the animals living in the building produce?

                var allAnimalItems = new List<string>();
                foreach (var animal in allAnimals.Values)
                {
                    if (animal.home == building)
                    {
                        var animalData = animal.GetAnimalData();
                        foreach (var item in animalData.ProduceItemIds)
                        {
                            if (!allAnimalItems.Contains(item.ItemId))
                            {
                                this.Monitor.Log($"[Auto-Grabber Vacuum] Will transfer item ID {item.ItemId} if found", LogLevel.Trace);
                                allAnimalItems.Add(item.ItemId);
                            }
                        }
                    }
                }

                // What items are inside the building?

                foreach (var objectDictionary in building.indoors.Value.objects)
                {
                    foreach (var objectKeyValue in objectDictionary)
                    {
                        var currentObject = objectKeyValue.Value;
                        this.Monitor.Log($"[Auto-Grabber Vacuum] Found item ID {currentObject.QualifiedItemId} = {currentObject.Name}", LogLevel.Trace);

                        // Can the item be produced by any of the animals in the building?
                        
                        if (!allAnimalItems.Contains(currentObject.ItemId) && !allAnimalItems.Contains(currentObject.QualifiedItemId))
                        {
                            continue;
                        }

                        // Try to add item to Auto-Grabber
                        //   * addItem() returns null if item was added successfully

                        if (autoGrabberInterior.addItem(currentObject) != null)
                        {
                            this.Monitor.Log($"[Auto-Grabber Vacuum] Unable to transfer {currentObject.Name} (Auto-Grabber may be full)", LogLevel.Debug);
                            continue;
                        }
                        this.Monitor.Log($"[Auto-Grabber Vacuum] Transferred {currentObject.Name}", LogLevel.Debug);
                        ++numberItemsMoved;

                        // Remove original item
                        //   * Remove() returns true if key was present and removed successfully (should only fail on a network race condition or something)

                        var currentLocation = objectKeyValue.Key;
                        if (!building.indoors.Value.objects.Remove(currentLocation))
                        {
                            this.Monitor.Log($"[Auto-Grabber Vacuum] Failed to remove {currentObject.Name} from original location {currentLocation.X}, {currentLocation.Y}", LogLevel.Error);
                        };
                    }
                }
            }

            // Display message summarizing results

            if (numberItemsMoved > 0 || manualRequest == true)
            {
                Game1.showGlobalMessage(Helper.Translation.Get("Message_VacuumResult", new { NumberItems = numberItemsMoved }));
            }
        }
    }
}
