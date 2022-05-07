// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Osu.Mods
{
    public class OsuModSolenoid : Mod, IUpdatableByPlayfield, IApplicableToDrawableRuleset<OsuHitObject>, IApplicableToPlayer
    {
        public override string Description => @"Monkey mode";
        public override string Name => "Monkey Mode";
        public override string Acronym => "MM";
        public override IconUsage? Icon => OsuIcon.PlayStyleKeyboard;
        public override ModType Type => ModType.Automation;
        public override double ScoreMultiplier => 1;
        public override Type[] IncompatibleMods => new[] { typeof(ModAutoplay), typeof(ModRelax) };

        [SettingSource("Fuckign uhghh dropdown", "where device")]
        public Bindable<OsuModMirror.MirrorType> Reflection { get; } = new Bindable<OsuModMirror.MirrorType>();

        /// <summary>
        /// How early before a hitobject's start time to trigger a hit.
        /// </summary>
        private const float relax_leniency = 3;

        private bool isDownState;
        private bool wasLeft = true;

        private OsuInputManager osuInputManager;

        //private ReplayState<OsuAction> state;
        private double lastStateChangeTime;

        private bool hasReplay;
        private bool connected;
        private SerialPort sp;

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            // grab the input manager for future use.
            osuInputManager = (OsuInputManager)drawableRuleset.KeyBindingInputManager;
        }

        public void ApplyToPlayer(Player player)
        {
            hasReplay = osuInputManager.ReplayInputHandler != null;

            if (!hasReplay)
            {
                string[] devices = SerialPort.GetPortNames();
                sp = new SerialPort(devices[0], 9600);

                try
                {
                    sp.Open();
                    sp.WriteTimeout = 30;
                }
                catch (Exception e)
                {
                    Logger.Error(e, e.Message);
                }
                finally
                {
                    connected = sp.IsOpen;
                }
            }
        }

        public void Update(Playfield playfield)
        {
            if (hasReplay)
                return;

            bool requiresHold = false;
            bool requiresHit = false;

            double time = playfield.Clock.CurrentTime;

            foreach (var h in playfield.HitObjectContainer.AliveObjects.OfType<DrawableOsuHitObject>())
            {
                // we are not yet close enough to the object.
                if (time < h.HitObject.StartTime - relax_leniency)
                    break;

                // already hit or beyond the hittable end time.
                if (h.IsHit || (h.HitObject is IHasDuration hasEnd && time > hasEnd.EndTime))
                    continue;

                switch (h)
                {
                    case DrawableHitCircle circle:
                        handleHitCircle(circle);
                        break;

                    case DrawableSlider slider:
                        // Handles cases like "2B" beatmaps, where sliders may be overlapping and simply holding is not enough.
                        if (!slider.HeadCircle.IsHit)
                            handleHitCircle(slider.HeadCircle);

                        requiresHold |= slider.Ball.IsHovered || h.IsHovered;
                        break;

                    case DrawableSpinner spinner:
                        requiresHold |= spinner.HitObject.SpinsRequired > 0;
                        break;
                }
            }

            if (requiresHit)
            {
                changeState(false);
                changeState(true);
            }

            if (requiresHold)
                changeState(true);
            else if (isDownState && time - lastStateChangeTime > AutoGenerator.KEY_UP_DELAY)
                changeState(false);

            void handleHitCircle(DrawableHitCircle circle)
            {
                if (!circle.HitArea.IsHovered)
                    return;

                Debug.Assert(circle.HitObject.HitWindows != null);
                requiresHit |= circle.HitObject.HitWindows.CanBeHit(time - circle.HitObject.StartTime);
            }

            void changeState(bool down)
            {
                if (isDownState == down || !connected)
                    return;

                isDownState = down;
                lastStateChangeTime = time;

                try
                {
                    if (down)
                    {
                        Logger.Log("TIME TO PRESS LE BUTTON", LoggingTarget.Runtime);
                        sp.Write(new byte[] { wasLeft ? (byte)1 : (byte)3 }, 0, 1);
                        wasLeft = !wasLeft;
                    }
                    else
                    {
                        Logger.Log("TIME TO UNPRESS LE BUTTON", LoggingTarget.Runtime);
                        sp.Write(new byte[] { 0, 2 }, 0, 2);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, e.Message);
                }
            }
        }
    }
}
