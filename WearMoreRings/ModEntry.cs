﻿using Netcode;
using StardewValley;
using StardewModdingAPI;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Ring = StardewValley.Objects.Ring;

namespace StardewHack.WearMoreRings
{
    using SaveRingsDict   = Dictionary<long, SaveRings>;

    #region Data Classes
    /// <summary>
    /// Structure used to store the actual rings.
    /// </summary> 
    public class ActualRings {
        public readonly NetRef<Ring> ring1 = new NetRef<Ring>(null);
        public readonly NetRef<Ring> ring2 = new NetRef<Ring>(null);
        public readonly NetRef<Ring> ring3 = new NetRef<Ring>(null);
        public readonly NetRef<Ring> ring4 = new NetRef<Ring>(null);
        
        public void LoadRings(SaveRings sr) {
            ring1.Set(MakeRing(sr.which1));
            ring2.Set(MakeRing(sr.which2));
            ring3.Set(MakeRing(sr.which3));
            ring4.Set(MakeRing(sr.which4));
        }
        
        private Ring MakeRing(int which) {
            if (which < 0) return null;
            return new Ring(which);
        }
    }
    
    /// <summary>
    /// Structure for save data.
    /// </summary>
    public class SaveRings {
        public int which1;
        public int which2;
        public int which3;
        public int which4;

        public SaveRings() { }
        
        public SaveRings(ActualRings er) {
            which1 = getWhich(er.ring1);
            which2 = getWhich(er.ring2);
            which3 = getWhich(er.ring3);
            which4 = getWhich(er.ring4);
        }
        
        private int getWhich(Ring r) {
            if (r==null) return -1;
            return r.ParentSheetIndex;
        }
    }
    #endregion Data Classes

    public class ModEntry : Hack<ModEntry>
    {
        static readonly ConditionalWeakTable<Farmer, ActualRings> actualdata = new ConditionalWeakTable<Farmer, ActualRings>();
        static IMonitor mon;
        
        public override void Entry(IModHelper helper) {
            base.Entry(helper);
            
            helper.Events.GameLoop.Saving += GameLoop_Saving;
            helper.Events.GameLoop.SaveLoaded += GameLoop_SaveLoaded;
            
            mon = Monitor;
        }
        
        static ActualRings FarmerNotFound(Farmer f) {
            throw new System.Exception("ERROR: A Farmer object was not correctly registered with the 'WearMoreRings' mod.");
        }
        
        #region Events
        /// <summary>
        /// Serializes the worn extra rings to disk.
        /// </summary>
        void GameLoop_Saving(object sender, StardewModdingAPI.Events.SavingEventArgs e) {
            var savedata = new SaveRingsDict();
            foreach(Farmer f in Game1.getAllFarmers()) {
                savedata[f.UniqueMultiplayerID] = new SaveRings(actualdata.GetValue(f, FarmerNotFound));
            }
            Helper.Data.WriteSaveData("extra-rings", savedata);
            Monitor.Log("Saved extra rings data.");
        }

        /// <summary>
        /// Reads the saved extra rings and creates them.
        /// </summary>
        void GameLoop_SaveLoaded(object sender, StardewModdingAPI.Events.SaveLoadedEventArgs e) {
            // Load data from mod's save file, if available.
            var savedata = Helper.Data.ReadSaveData<SaveRingsDict>("extra-rings");
            if (savedata == null) {
                Monitor.Log("Save data not available.");
                return;
            }
            // Iterate through each farmer to load the extra equipped rings.
            foreach(Farmer f in Game1.getAllFarmers()) {
                if (savedata.ContainsKey(f.UniqueMultiplayerID)) {
                    actualdata.GetValue(f, FarmerNotFound).LoadRings(savedata[f.UniqueMultiplayerID]);
                }
            }
            Monitor.Log("Loaded extra rings save data.");
        }
        #endregion Events
        
        #region Patch Farmer
        /// <summary>
        /// Add the extra rings to the Netcode tree.
        /// </summary>
        public static void InitFarmer(Farmer f) {
            var actualrings = new ActualRings();
            f.NetFields.AddFields(
                actualrings.ring1,
                actualrings.ring2,
                actualrings.ring3,
                actualrings.ring4
            );
            actualdata.Add(f, actualrings);
        }

        [BytecodePatch("StardewValley.Farmer::farmerInit")]
        void Farmer_farmerInit() {
            var addfields = FindCode(
                OpCodes.Stelem_Ref,
                Instructions.Callvirt(typeof(NetFields), "AddFields", typeof(INetSerializable[]))
            );
            addfields.Append(
                Instructions.Ldarg_0(),
                Instructions.Call(typeof(ModEntry), "InitFarmer", typeof(Farmer))
            );
        }
        
        public static int CountWearingRing(Farmer f, int id) {
            bool IsRing(Ring r) {
                return r != null && r.parentSheetIndex == id;
            }
        
            ActualRings ar = actualdata.GetValue(f, FarmerNotFound);
            int res = 0;
            if (IsRing(f.leftRing)) res++;
            if (IsRing(f.rightRing)) res++;
            if (IsRing(ar.ring1)) res++;
            if (IsRing(ar.ring2)) res++;
            if (IsRing(ar.ring3)) res++;
            if (IsRing(ar.ring4)) res++;
            return res;
        }

        [BytecodePatch("StardewValley.Farmer::isWearingRing")]
        void Farmer_isWearingRing() {
            AllCode().Replace(
                Instructions.Ldarg_0(),
                Instructions.Ldarg_1(),
                Instructions.Call(typeof(ModEntry), "CountWearingRing", typeof(Farmer), typeof(int)),
                Instructions.Ret()
            );
        }

        public static void UpdateRings(GameTime time, GameLocation location, Farmer f) {
            void update(Ring r) { 
                if (r != null) r.update(time, location, f); 
            };
            
            ActualRings ar = actualdata.GetValue(f, FarmerNotFound);
            update(f.leftRing);
            update(f.rightRing);
            update(ar.ring1);
            update(ar.ring2);
            update(ar.ring3);
            update(ar.ring4);
        }
        
        [BytecodePatch("StardewValley.Farmer::updateCommon")]
        void Farmer_updateCommon() {
            FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Farmer), "rightRing"),
                OpCodes.Callvirt,
                OpCodes.Brfalse,
                
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Farmer), "rightRing"),
                OpCodes.Callvirt,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_2,
                OpCodes.Ldarg_0,
                OpCodes.Callvirt,
                
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Farmer), "leftRing"),
                OpCodes.Callvirt,
                OpCodes.Brfalse,
                
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Farmer), "leftRing"),
                OpCodes.Callvirt,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_2,
                OpCodes.Ldarg_0,
                OpCodes.Callvirt
            ).Replace(
                Instructions.Ldarg_1(),
                Instructions.Ldarg_2(),
                Instructions.Ldarg_0(),
                Instructions.Call(typeof(ModEntry), "UpdateRings", typeof (GameTime), typeof(GameLocation), typeof(Farmer)),
                Instructions.Ret()
            );
        }
        #endregion Patch Farmer

        #region Patch GameLocation
        
        #endregion Patch GameLocation
        
        #region Patch InventoryPage
        static public void AddEquipmentIcon(StardewValley.Menus.InventoryPage page, int x, int y, string name) {
            var rect = new Rectangle (
                page.xPositionOnScreen + 48 + x*64, 
                page.yPositionOnScreen + StardewValley.Menus.IClickableMenu.borderWidth + StardewValley.Menus.IClickableMenu.spaceToClearTopBorder + 256 - 12 + y*64, 
                64, 64
            );
            
            // Get the item that should be in this slot.
            Item item = null;
            if (x == 0) { 
                if (y == 0) item = Game1.player.hat.Value;
                if (y == 1) item = Game1.player.leftRing.Value;
                if (y == 2) item = Game1.player.rightRing.Value;
                if (y == 3) item = Game1.player.boots.Value;
            } else {
                ActualRings ar = actualdata.GetValue(Game1.player, FarmerNotFound);
                if (y == 0) item = ar.ring1.Value;
                if (y == 1) item = ar.ring2.Value;
                if (y == 2) item = ar.ring3.Value;
                if (y == 3) item = ar.ring4.Value;
            }
            
            // Create the GUI element.
            int id = 101+10*x+y;
            var component = new StardewValley.Menus.ClickableComponent(rect, name) {
                myID = id,
                downNeighborID = y<3 ? id+1 : -1,
                upNeighborID = y==0 ? Game1.player.MaxItems - 12 + x : id-1,
                upNeighborImmutable = y==0,
                rightNeighborID = x==0 ? id+10 : 105,
                leftNeighborID = x==0 ? -1 : id-10,
                item = item
            };
            page.equipmentIcons.Add(component);
        }
        
        static string[] EquipmentIcons = {
            "Hat",
            "Left Ring",
            "Right Ring",
            "Boots",
            "Extra Ring 1",
            "Extra Ring 2",
            "Extra Ring 3",
            "Extra Ring 4",
        };
        
        [BytecodePatch("StardewValley.Menus.InventoryPage::.ctor(System.Int32,System.Int32,System.Int32,System.Int32)")]
        void InventoryPage_ctor() {
            // Replace code for equipment icon creation with method calls to our AddEquipmentIcon method.
            var items = FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(StardewValley.Menus.InventoryPage), "equipmentIcons"),
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(StardewValley.Menus.IClickableMenu), "xPositionOnScreen"),
                Instructions.Ldc_I4_S(48)
            );
            items.Extend(
                OpCodes.Dup,
                Instructions.Ldc_I4_S(104),
                Instructions.Stfld(typeof(StardewValley.Menus.ClickableComponent), "myID"),
                OpCodes.Dup,
                Instructions.Ldc_I4_S(105),
                Instructions.Stfld(typeof(StardewValley.Menus.ClickableComponent), "rightNeighborID"),
                OpCodes.Callvirt
            );
            items.Remove();
            for (int i=0; i<EquipmentIcons.Length; i++) {
                items.Append(
                    Instructions.Ldarg_0(),                 // page
                    Instructions.Ldc_I4_S((byte)(i/4)),     // x
                    Instructions.Ldc_I4_S((byte)(i%4)),     // y
                    Instructions.Ldstr(EquipmentIcons[i]),  // name
                    Instructions.Call(typeof(ModEntry), "AddEquipmentIcon", typeof(StardewValley.Menus.InventoryPage), typeof(int), typeof(int), typeof(string))
                );
            }
            
            // Move portrait 64px to the right.
            // This only affects where the tooltip shows up.
            FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(StardewValley.Menus.IClickableMenu), "xPositionOnScreen"),
                Instructions.Ldc_I4(192),
                OpCodes.Add,
                Instructions.Ldc_I4_S(64),
                OpCodes.Sub,
                Instructions.Ldc_I4_S(32),
                OpCodes.Add
            ).SubRange(4,2).Remove();
        }
        
        static public void DrawEquipment(StardewValley.Menus.ClickableComponent icon, Microsoft.Xna.Framework.Graphics.SpriteBatch b) {
            if (icon.item != null) {
                b.Draw(Game1.menuTexture, icon.bounds, Game1.getSourceRectForStandardTileSheet (Game1.menuTexture, 10, -1, -1), Color.White);
                icon.item.drawInMenu(b, new Vector2(icon.bounds.X, icon.bounds.Y), icon.scale, 1f, 0.866f, false);
            } else {
                int tile = 41;
                if (icon.name == "Hat") tile = 42;
                if (icon.name == "Boots") tile = 40;
                b.Draw (Game1.menuTexture, icon.bounds, Game1.getSourceRectForStandardTileSheet (Game1.menuTexture, tile, -1, -1), Color.White);
            }
        }
        
        [BytecodePatch("StardewValley.Menus.InventoryPage::draw")]
        void InventoryPage_draw() {
            // Change the equipment slot drawing code to draw the 4 additional slots.
            object[] loop_start = {
                Instructions.Ldloca_S(3),
                OpCodes.Call,
                Instructions.Stloc_S(4),
                Instructions.Ldloc_S(4),
                Instructions.Ldfld(typeof(StardewValley.Menus.ClickableComponent), "name"),
                Instructions.Stloc_S(5),
                Instructions.Ldloc_S(5),
                Instructions.Ldstr("Hat")
            };
            var range = FindCode(loop_start).Follow(-1);
            range.ExtendBackwards(loop_start);
            range.Replace(
                range[0],
                range[1],
                Instructions.Ldarg_1(),
                Instructions.Call(typeof(ModEntry), "DrawEquipment", typeof(StardewValley.Menus.ClickableComponent), typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch))
            );
            
            // Move other stuff 32/64px to the right to eliminate overlap.
            for (int i=0; i<11; i++) {
                range = range.FindNext(
                    OpCodes.Ldarg_0,
                    Instructions.Ldfld(typeof(StardewValley.Menus.IClickableMenu), "xPositionOnScreen"),
                    OpCodes.Ldc_I4,
                    OpCodes.Add
                );
                int val = (int)range[2].operand + 32;
                if (val < 256) val = 256;
                range[2].operand = val;
            }
        }
        
        [BytecodePatch("StardewValley.Menus.InventoryPage::performHoverAction")]
        void InventoryPage_performHoverAction() {
            // Change code responsible for obtaining the tooltip information.
            var var_item = generator.DeclareLocal(typeof(Item));
            var code = FindCode(
                OpCodes.Ldloc_1,
                Instructions.Ldfld(typeof(StardewValley.Menus.ClickableComponent), "name"),
                OpCodes.Stloc_2,
                OpCodes.Ldloc_2,
                Instructions.Ldstr("Hat")
            );
            code.Extend(
                OpCodes.Ldarg_0,
                Instructions.Call_get(typeof(Game1), "player"),
                Instructions.Ldfld(typeof(Farmer), "boots"),
                OpCodes.Callvirt,
                Instructions.Callvirt_get(typeof(Item), "DisplayName"),
                Instructions.Stfld(typeof(StardewValley.Menus.InventoryPage), "hoverTitle")
            );
            code.Replace(
                // var item = EquipmentIcon.item
                Instructions.Ldloc_1(),
                Instructions.Ldfld(typeof(StardewValley.Menus.ClickableComponent), "item"),
                Instructions.Stloc_S(var_item),
                // if (item != null)
                Instructions.Ldloc_S(var_item),
                Instructions.Brfalse(AttachLabel(code.End[0])),
                // hoveredItem = item;
                Instructions.Ldarg_0(),
                Instructions.Ldloc_S(var_item),
                Instructions.Stfld(typeof(StardewValley.Menus.InventoryPage), "hoveredItem"),
                // hoverText = item.getDescription();
                Instructions.Ldarg_0(),
                Instructions.Ldloc_S(var_item),
                Instructions.Callvirt(typeof(Item), "getDescription"),
                Instructions.Stfld(typeof(StardewValley.Menus.InventoryPage), "hoverText"),
                // hoverTitle = item.DisplayName;
                Instructions.Ldarg_0(),
                Instructions.Ldloc_S(var_item),
                Instructions.Callvirt_get(typeof(Item), "DisplayName"),
                Instructions.Stfld(typeof(StardewValley.Menus.InventoryPage), "hoverTitle")
            );
        }
        
        #endregion Patch InventoryPage
        
    }
}
