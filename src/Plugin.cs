﻿using System;
using BepInEx;
using UnityEngine;
using SlugBase.Features;
using static SlugBase.Features.FeatureTypes;
using DressMySlugcat;
using RWCustom;
using SlugBase;
using System.Collections.Generic;
using System.Linq;
using ImprovedInput;
using System.IO;

namespace SlugTemplate
{
    [BepInDependency("slime-cubed.slugbase")]
    [BepInDependency("dressmyslugcat", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(MOD_ID, "The Vinki", "0.1.0")]
    class Plugin : BaseUnityPlugin
    {
        private const string MOD_ID = "olaycolay.thevinki";
        private int lastXDirection = 1;
        private int lastYDirection = 1;
        private bool grindUpPoleFlag = false;
        private bool isGrindingH = false;
        private bool isGrindingV = false;
        private bool isGrindingNoGrav = false;
        private bool isGrindingVine = false;
        private Vector2 lastVineDir = Vector2.zero;
        private Player.AnimationIndex lastAnimationFrame = Player.AnimationIndex.None;
        private Player.AnimationIndex lastAnimation = Player.AnimationIndex.None;
        private ChunkSoundEmitter grindSound;
        private List<PlacedObject.CustomDecalData> graffitis = new List<PlacedObject.CustomDecalData>();

        public static readonly string graffitiFolder = "RainWorld_Data/StreamingAssets/decals/VinkiGraffiti";
        public static readonly PlayerFeature<float> CoyoteBoost = PlayerFloat("thevinki/coyote_boost");
        public static readonly PlayerFeature<float> GrindXSpeed = PlayerFloat("thevinki/grind_x_speed");
        public static readonly PlayerFeature<float> GrindVineSpeed = PlayerFloat("thevinki/grind_vine_speed");
        public static readonly PlayerFeature<float> GrindYSpeed = PlayerFloat("thevinki/grind_y_speed");
        public static readonly PlayerFeature<float> NormalXSpeed = PlayerFloat("thevinki/normal_x_speed");
        public static readonly PlayerFeature<float> NormalYSpeed = PlayerFloat("thevinki/normal_y_speed");
        public static readonly PlayerFeature<float> SuperJump = PlayerFloat("thevinki/super_jump");
        public static readonly PlayerFeature<Color> SparkColor = PlayerColor("thevinki/spark_color");

        public static readonly PlayerKeybind Graffiti = PlayerKeybind.Register("thevinki:graffiti", "The Vinki", "Graffiti Mode", KeyCode.C, KeyCode.JoystickButton4);

        // Add hooks
        public void OnEnable()
        {
            On.RainWorld.OnModsInit += Extras.WrapInit(LoadResources);
            On.RainWorld.PostModsInit += RainWorld_PostModsInit;

            // Put your custom hooks here!
            On.Player.Jump += Player_Jump;
            On.Player.MovementUpdate += Player_Move;
            On.Player.Update += Player_Update;
        }

        // Load any resources, such as sprites or sounds
        private void LoadResources(RainWorld rainWorld)
        {
            string parent = Path.GetFileNameWithoutExtension(graffitiFolder);

            foreach (var image in Directory.EnumerateFiles(graffitiFolder, "*.*", SearchOption.AllDirectories)
            .Where(s => s.EndsWith(".png")).Select(f => parent + '/' + Path.GetFileNameWithoutExtension(f)))
            {
                PlacedObject.CustomDecalData decal = new PlacedObject.CustomDecalData(null);
                decal.imageName = image;
                graffitis.Add(decal);
            }
        }

        public static bool IsPostInit;
        private void RainWorld_PostModsInit(On.RainWorld.orig_PostModsInit orig, RainWorld self)
        {
            orig(self);
            try
            {
                if (IsPostInit) return;
                IsPostInit = true;

                //-- You can have the DMS sprite setup in a separate method and only call it if DMS is loaded
                //-- With this the mod will still work even if DMS isn't installed
                if (ModManager.ActiveMods.Any(mod => mod.id == "dressmyslugcat"))
                {
                    SetupDMSSprites();
                }

                Debug.Log($"Plugin dressmyslugcat.templatecat is loaded!");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public void SetupDMSSprites()
        {
            //-- The ID of the spritesheet we will be using as the default sprites for our slugcat
            var sheetID = "olaycolay.thevinki";

            //-- Each player slot (0, 1, 2, 3) can be customized individually
            for (int i = 0; i < 4; i++)
            {
                SpriteDefinitions.AddSlugcatDefault(new Customization()
                {
                    //-- Make sure to use the same ID as the one used for our slugcat
                    Slugcat = "TheVinki",
                    PlayerNumber = i,
                    CustomSprites = new List<CustomSprite>
                    {
                        //-- You can customize which spritesheet and color each body part will use
                        new CustomSprite() { Sprite = "HEAD", SpriteSheetID = sheetID },
                        new CustomSprite() { Sprite = "FACE", SpriteSheetID = sheetID },
                        new CustomSprite() { Sprite = "BODY", SpriteSheetID = sheetID },
                        new CustomSprite() { Sprite = "ARMS", SpriteSheetID = sheetID },
                        new CustomSprite() { Sprite = "HIPS", SpriteSheetID = sheetID },
                        new CustomSprite() { Sprite = "LEGS", SpriteSheetID = sheetID },
                        new CustomSprite() { Sprite = "TAIL", SpriteSheetID = sheetID }
                    }
                });
            }
        }

        // Implement SuperJump
        private void Player_Jump(On.Player.orig_Jump orig, Player self)
        {
            // Don't jump off a pole when we're trying to do a trick jump at the top
            if (isGrindingV && !grindUpPoleFlag && self.input[0].x == 0f && lastYDirection > 0)
            {
                return;
            }

            orig(self);

            if (!SuperJump.TryGet(self, out float power) || !CoyoteBoost.TryGet(self, out var coyoteBoost))
            {
                return;
            }

            // If player jumped or coyote jumped from a beam (or grinded to top of pole), then trick jump
            bool coyote = isCoyoteJumping(self);
            if (coyote || isGrindingH || grindUpPoleFlag)
            {
                // Get num multiplier
                float num = Mathf.Lerp(1f, 1.15f, self.Adrenaline);
                if (self.grasps[0] != null && self.HeavyCarry(self.grasps[0].grabbed) && !(self.grasps[0].grabbed is Cicada))
                {
                    num += Mathf.Min(Mathf.Max(0f, self.grasps[0].grabbed.TotalMass - 0.2f) * 1.5f, 1.3f);
                }

                // Initiate flip
                if (self.PainJumps)
                {
                    self.bodyChunks[0].vel.y = 4f * num;
                    self.bodyChunks[1].vel.y = 3f * num;
                }
                else
                {
                    self.bodyChunks[0].vel.y = 9f * num;
                    self.bodyChunks[1].vel.y = 7f * num;
                }
                self.slideDirection = lastXDirection;

                // Get risk/reward speedboost when coyote jumping
                if (coyote)
                {
                    //Debug.Log("Coyote jump");
                    self.mainBodyChunk.vel.x += coyoteBoost * self.slideDirection;
                    self.room.PlaySound(SoundID.Slugcat_Flip_Jump, self.mainBodyChunk, false, 3f, 1f);
                }
                else
                {
                    self.room.PlaySound(SoundID.Slugcat_Flip_Jump, self.mainBodyChunk, false, 1f, 1f);
                }

                self.jumpBoost *= power;
                self.animation = Player.AnimationIndex.Flip;
                self.slideCounter = 0;

                grindUpPoleFlag = false;
            }
        }

        private bool isCoyoteJumping(Player self)
        {
            //Debug.Log("Last Animation: " + lastAnimation + "\t This animation: " + self.animation + 
            //        "\tLast frame: " + lastAnimationFrame + "\tcanJump frames: " + self.canJump +
            //        "\tBody mode: " + self.bodyMode);
            return (lastAnimation == Player.AnimationIndex.StandOnBeam && self.animation == Player.AnimationIndex.None &&
                    self.bodyMode == Player.BodyModeIndex.Default);
        }

        // Implement higher beam speed
        private void Player_Move(On.Player.orig_MovementUpdate orig, Player self, bool eu)
        {
            orig(self, eu);

            if (!GrindXSpeed.TryGet(self, out var grindXSpeed) || !NormalXSpeed.TryGet(self, out var normalXSpeed) ||
                !GrindYSpeed.TryGet(self, out var grindYSpeed) || !NormalYSpeed.TryGet(self, out var normalYSpeed) ||
                !SparkColor.TryGet(self, out var sparkColor) || !GrindVineSpeed.TryGet(self, out var grindVineSpeed))
            {
                return;
            }

            // Save the last direction that Vinki was facing
            if (self.input[0].x != 0)
            {
                lastXDirection = self.input[0].x;
            }
            if (self.input[0].y != 0)
            {
                lastYDirection = self.input[0].y;
            }
            if (self.SwimDir(true).magnitude > 0f)
            {
                lastVineDir = self.SwimDir(true);
            }
            // Save the last animation
            if (self.animation != lastAnimationFrame)
            {
                lastAnimation = lastAnimationFrame;
                lastAnimationFrame = self.animation;
            }

            // If grinding up a pole and reach the top, jump up high
            if (self.animation == Player.AnimationIndex.GetUpToBeamTip && isGrindingV)
            {
                if (self.input[0].jmp)
                {
                    grindUpPoleFlag = true;
                    self.Jump();
                }
                else
                {
                    grindUpPoleFlag = false;
                    self.bodyChunks[1].pos = self.room.MiddleOfTile(self.bodyChunks[1].pos) + new Vector2(0f, 5f);
                    self.bodyChunks[1].vel *= 0f;
                    self.bodyChunks[0].vel = Vector2.ClampMagnitude(self.bodyChunks[0].vel, 9f);
                }
            }
            else
            {
                grindUpPoleFlag = false;
            }

            // If player isn't holding pckp, no need to do other stuff
            if (!self.input[0].pckp)
            {
                isGrindingH = isGrindingV = isGrindingNoGrav = isGrindingVine = false;
                self.slugcatStats.runspeedFac = normalXSpeed;
                self.slugcatStats.poleClimbSpeedFac = normalYSpeed;
                return;
            }

            isGrindingH = IsGrindingHorizontally(self);
            isGrindingV = IsGrindingVertically(self);
            isGrindingNoGrav = IsGrindingNoGrav(self);
            isGrindingVine = IsGrindingVine(self);

            // Grind horizontally if holding pckp on a beam
            if (isGrindingH)
            {
                self.slugcatStats.runspeedFac = 0;
                self.bodyChunks[1].vel.x = grindXSpeed * lastXDirection;

                // Sparks from grinding
                Vector2 pos = self.bodyChunks[1].pos;
                Vector2 posB = pos - new Vector2(10f * lastXDirection, 0);
                for (int j = 0; j < 2; j++)
                {
                    Vector2 a = RWCustom.Custom.RNV();
                    a.x = Mathf.Abs(a.x) * -lastXDirection;
                    a.y = Mathf.Abs(a.y);
                    self.room.AddObject(new Spark(pos, a * Mathf.Lerp(4f, 30f, UnityEngine.Random.value), sparkColor, null, 2, 4));
                    self.room.AddObject(new Spark(posB, a * Mathf.Lerp(4f, 30f, UnityEngine.Random.value), sparkColor, null, 2, 4));
                }

                // Looping grind sound
                PlayGrindSound(self);
            }
            else
            {
                self.slugcatStats.runspeedFac = normalXSpeed;
            }

            // Grind if holding pckp on a pole (vertical beam or 0G beam or vine)
            if (isGrindingV || isGrindingNoGrav || isGrindingVine)
            {
                Debug.Log("Zero G Pole direction: " + self.zeroGPoleGrabDir.x + "," + self.zeroGPoleGrabDir.y);
                self.slugcatStats.poleClimbSpeedFac = 0;

                // Handle vine grinding
                if (isGrindingVine)
                {
                    self.vineClimbCursor = grindVineSpeed * Vector2.ClampMagnitude(self.vineClimbCursor + lastVineDir * Custom.LerpMap(Vector2.Dot(lastVineDir, self.vineClimbCursor.normalized), -1f, 1f, 10f, 3f), 30f);
                    Vector2 a6 = self.room.climbableVines.OnVinePos(self.vinePos);
                    self.vinePos.floatPos += self.room.climbableVines.ClimbOnVineSpeed(self.vinePos, self.mainBodyChunk.pos + self.vineClimbCursor) * Mathf.Lerp(2.1f, 1.5f, self.EffectiveRoomGravity) / self.room.climbableVines.TotalLength(self.vinePos.vine);
                    self.vinePos.floatPos = Mathf.Clamp(self.vinePos.floatPos, 0f, 1f);
                    self.room.climbableVines.PushAtVine(self.vinePos, (a6 - self.room.climbableVines.OnVinePos(self.vinePos)) * 0.05f);
                    if (self.vineGrabDelay == 0 && (!ModManager.MMF || !self.GrabbedByDaddyCorruption))
                    {
                        ClimbableVinesSystem.VinePosition vinePosition = self.room.climbableVines.VineSwitch(self.vinePos, self.mainBodyChunk.pos + self.vineClimbCursor, self.mainBodyChunk.rad);
                        if (vinePosition != null)
                        {
                            self.vinePos = vinePosition;
                            self.vineGrabDelay = 10;
                        }
                    }
                }
                else
                {
                    // Handle 0G horizontal beam grinding
                    if (isGrindingNoGrav && self.room.GetTile(self.mainBodyChunk.pos).horizontalBeam)
                    {
                        self.bodyChunks[0].vel.x = grindYSpeed * lastXDirection;
                    }
                    else
                    {
                        // This works in gravity and no gravity
                        self.bodyChunks[0].vel.y = grindYSpeed * lastYDirection;
                    }
                }

                // Sparks from grinding
                Vector2 pos = (self.graphicsModule as PlayerGraphics).hands[0].pos;
                Vector2 posB = (self.graphicsModule as PlayerGraphics).hands[1].pos;
                for (int j = 0; j < 2; j++)
                {
                    Vector2 a = RWCustom.Custom.RNV();
                    a.x = Mathf.Abs(a.x) * lastXDirection;
                    a.y = Mathf.Abs(a.y) * -lastYDirection;
                    self.room.AddObject(new Spark(pos, a * Mathf.Lerp(4f, 30f, UnityEngine.Random.value), sparkColor, null, 2, 4));
                    self.room.AddObject(new Spark(posB, a * Mathf.Lerp(4f, 30f, UnityEngine.Random.value), sparkColor, null, 2, 4));
                }

                // Looping grind sound
                PlayGrindSound(self);
            }
            else
            {
                self.slugcatStats.poleClimbSpeedFac = normalYSpeed;
            }

            // Catch beam with feet if not holding down
            if (self.bodyMode == Player.BodyModeIndex.Default && 
                self.input[0].y >= 0 && self.room.GetTile(self.bodyChunks[1].pos).horizontalBeam &&
                self.bodyChunks[0].vel.y < 0f)
            {
                self.room.PlaySound(SoundID.Spear_Bounce_Off_Creauture_Shell, self.mainBodyChunk, false, 0.75f, 1f);
                self.noGrabCounter = 15;
                self.animation = Player.AnimationIndex.StandOnBeam;
                self.bodyChunks[1].pos.y = self.room.MiddleOfTile(self.bodyChunks[1].pos).y + 5f;
                self.bodyChunks[1].vel.y = 0f;
                self.bodyChunks[0].vel.y = 0f;
            }

            // Stop flipping when falling fast and letting go of jmp (so landing on a rail doesn't look weird)
            if (self.animation == Player.AnimationIndex.Flip)
            {
                if (self.bodyChunks[0].vel.y < -3f && !self.input[0].jmp)
                {
                    self.animation = Player.AnimationIndex.HangFromBeam;
                }
            }
        }

        private void PlayGrindSound(Player self)
        {
            if (grindSound == null || grindSound.currentSoundObject == null || grindSound.currentSoundObject.slatedForDeletion)
            {
                grindSound = self.room.PlaySound(SoundID.Shelter_Gasket_Mover_LOOP, self.mainBodyChunk, true, 0.15f, 10f);
                grindSound.requireActiveUpkeep = true;
            }
            grindSound.alive = true;
        }

        private bool IsGrindingHorizontally(Player self)
        {
            return (self.animation == Player.AnimationIndex.StandOnBeam && 
                self.bodyChunks[0].vel.magnitude > 3f);
        }

        private bool IsGrindingVertically(Player self)
        {
            return (self.animation == Player.AnimationIndex.ClimbOnBeam && 
                ((lastYDirection > 0 && self.bodyChunks[1].vel.magnitude > 2f) ||
                (lastYDirection < 0 && self.bodyChunks[1].vel.magnitude > 1f)));
        }

        private bool IsGrindingNoGrav(Player self)
        {
            return (self.animation == Player.AnimationIndex.ZeroGPoleGrab &&
                self.bodyChunks[0].vel.magnitude > 1f);
        }

        private bool IsGrindingVine(Player self)
        {
            return (self.animation == Player.AnimationIndex.VineGrab && 
                self.bodyChunks[0].vel.magnitude > 1f);
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            if (self.input[0].pckp && !self.input[1].pckp && self.IsPressed(Graffiti))
            {
                PlacedObject graffiti = new PlacedObject(PlacedObject.Type.CustomDecal, graffitis[0]);
                graffiti.pos = self.mainBodyChunk.pos;

                for (int i = 0; i < 5; i++)
                {
                    self.room.AddObject(new CustomDecal(graffiti));
                }
            }
        }
    }
}