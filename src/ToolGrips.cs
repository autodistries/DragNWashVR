using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace WalkNWash.VRCompanion
{
    internal sealed class ToolGrips
    {
        private sealed class Grip
        {
            internal ConfigEntry<Vector3> Position, Rotation;
        }
        private readonly ConfigFile config;
        private string previewName;
        private Vector3 previewPosition, previewRotation;
        private readonly Dictionary<string, Grip> grips = new Dictionary<string, Grip>(StringComparer.OrdinalIgnoreCase);
        internal ToolGrips(ConfigFile config)
        {
            this.config = config;
            Add(config, "Crowbar", new Vector3(-0.0342881307f, 0.24586384f, 0.965012908f), new Vector3(54.5247231f, 23.2106266f, 25.0203285f));
            Add(config, "Sponge", new Vector3(0.0389792733f, -0.0409567505f, 0.115976587f), new Vector3(345.411774f, 107.809669f, 340.888824f));
            Add(config, "Sprayer", new Vector3(0.00093032961f, -0.127666786f, -0.210475251f), new Vector3(12.1314287f, 0.486448586f, 3.46904159f));
            Add(config, "Medkit", new Vector3(0.205404297f, -0.443058491f, 0.158783212f), new Vector3(348.631439f, 4.31504822f, 18.4467945f));
            Add(config, "Spinner", new Vector3(-0.0161585025f, 0.0383101068f, -0.250951409f), new Vector3(11.1664686f, 358.667908f, 355.184235f));
            Add(config, "Footstool", new Vector3(0.583419204f, 0.089880392f, -0.188502014f), new Vector3(15.8011942f, 6.94211292f, 10.5628586f));
            Add(config, "Bucket", new Vector3(-0.148726374f, -0.248128131f, -0.103323728f), new Vector3(25.53438f, 339.674805f, 352.01236f));
            Add(config, "ForestDebris", new Vector3(0.0570607111f, -0.0585235357f, 0.133772641f), new Vector3(296.980011f, 272.755127f, 17.650856f));
            Add(config, "Shoes", new Vector3(-0.102862097f, -0.231017902f, 0.132335752f), new Vector3(339.169769f, 248.59642f, 315.063538f));
            Add(config, "Onahole", new Vector3(-0.0143391201f, 0.0319253877f, 0.0489587039f), new Vector3(339.53067f, 1.54010057f, 314.164276f));
            Add(config, "Mount", new Vector3(-0.698649287f, 0.818435311f, -0.0469357669f), new Vector3(48.9974403f, 29.3306522f, 317.927185f));
            Add(config, "DateFoodBasket", new Vector3(-0.111241683f, -0.60368818f, 0.366499186f), new Vector3(350.549835f, 355.635437f, 0.480748206f));
            Add(config, "Niku", new Vector3(-0.000394951145f, 0.0723851919f, 0.0914122984f), new Vector3(349.618896f, 323.973602f, 72.4515305f));
            Add(config, "Egg", new Vector3(-0.0864470527f, -0.173150733f, 0.145319462f), new Vector3(0.100890487f, 314.194916f, 313.384338f));
            Add(config, "Watermelon", new Vector3(0.0477282405f, -0.295647591f, 0.170653269f), new Vector3(333.806183f, 264.036804f, 96.0563354f));
            Add(config, "Grapes", new Vector3(-0.253058314f, 0.282903582f, 0.678214431f), new Vector3(293.913635f, 182.07695f, 285.366882f));
        }
        private void Add(ConfigFile config, string name, Vector3 position, Vector3 rotation)
        {
            grips.Add(name, new Grip {
                Position = config.Bind("Tool Grips", name + " Position", position,
                    "Controller-local offset in meters before VR scaling: X right, Y up, Z forward. Sprayer is the water gun; Footstool is the small ladder."),
                Rotation = config.Bind("Tool Grips", name + " Rotation", rotation,
                    "Local Euler degrees: X pitch, Y yaw, Z roll. Applies only to this equipped tool.")
            });
        }
        internal void Read(string name, out Vector3 position, out Vector3 rotation, bool defaults = false)
        {
            if (!grips.TryGetValue(name, out var grip)) { Add(config, name, Vector3.zero, Vector3.zero); grip = grips[name]; }
            position = defaults ? (Vector3)grip.Position.DefaultValue : grip.Position.Value;
            rotation = defaults ? (Vector3)grip.Rotation.DefaultValue : grip.Rotation.Value;
        }
        internal void Preview(string name, Vector3 position, Vector3 rotation)
        { previewName = name; previewPosition = position; previewRotation = rotation; }
        internal void Finish(bool save)
        {
            if (save && previewName != null)
            {
                var grip = grips[previewName];
                grip.Position.Value = previewPosition;
                grip.Rotation.Value = previewRotation;
                config.Save();
            }
            previewName = null;
        }
        internal HandFrame Apply(string toolName, HandFrame frame, float scale)
        {
            if (toolName == null || !grips.TryGetValue(toolName, out var grip)) return frame;
            // Never accumulate on the previous model pose; every update starts at the controller.
            frame.Position += frame.Rotation * ((toolName == previewName ? previewPosition : grip.Position.Value) * scale);
            frame.Rotation *= Quaternion.Euler(toolName == previewName ? previewRotation : grip.Rotation.Value);
            // The sponge's assisted aiming ray stays controller-directed. Contact
            // still supplies its own surface normal; this only adjusts its grip pose.
            return frame;
        }
    }
}
