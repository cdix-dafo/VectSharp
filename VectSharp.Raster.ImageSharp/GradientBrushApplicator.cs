/*
    VectSharp - A light library for C# vector graphics.
    Copyright (C) 2020-2022 Giorgio Bianchini

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, version 3.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program. If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp;

namespace VectSharp.Raster.ImageSharp
{
    // Adapted from https://github.com/SixLabors/ImageSharp.Drawing/blob/main/src/ImageSharp.Drawing/Processing/GradientBrush.cs - Why isn't this public? D:
    internal abstract class GradientBrushApplicator<TPixel> : BrushRenderer<TPixel>
             where TPixel : unmanaged, IPixel<TPixel>
    {
        private static readonly TPixel Transparent = Color.Transparent.ToPixel<TPixel>();

        private readonly ColorStop[] colorStops;

        private readonly GradientRepetitionMode repetitionMode;

        private readonly PixelBlender<TPixel> blender;

        /// <summary>
        /// Initializes a new instance of the <see cref="GradientBrushApplicator{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="colorStops">An array of color stops sorted by their position.</param>
        /// <param name="repetitionMode">Defines if and how the gradient should be repeated.</param>
        protected GradientBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, canvasWidth)
        {
            // TODO: requires colorStops to be sorted by position.
            // Use Array.Sort with a custom comparer.
            this.colorStops = colorStops;
            this.repetitionMode = repetitionMode;
            this.blender = PixelOperations<TPixel>.Instance.GetPixelBlender(options);
        }

        internal TPixel this[int x, int y]
        {
            get
            {
                float positionOnCompleteGradient = this.PositionOnGradient(x + 0.5f, y + 0.5f);

                switch (this.repetitionMode)
                {
                    case GradientRepetitionMode.None:
                        // do nothing. The following could be done, but is not necessary:
                        // onLocalGradient = Math.Min(0, Math.Max(1, onLocalGradient));
                        break;
                    case GradientRepetitionMode.Repeat:
                        positionOnCompleteGradient %= 1;
                        break;
                    case GradientRepetitionMode.Reflect:
                        positionOnCompleteGradient %= 2;
                        if (positionOnCompleteGradient > 1)
                        {
                            positionOnCompleteGradient = 2 - positionOnCompleteGradient;
                        }

                        break;
                    case GradientRepetitionMode.DontFill:
                        if (positionOnCompleteGradient > 1 || positionOnCompleteGradient < 0)
                        {
                            return Transparent;
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                (ColorStop from, ColorStop to) = this.GetGradientSegment(positionOnCompleteGradient);

                if (from.Color.Equals(to.Color))
                {
                    return from.Color.ToPixel<TPixel>();
                }

                float onLocalGradient = (positionOnCompleteGradient - from.Ratio) / (to.Ratio - from.Ratio);

                return Color.FromScaledVector(Vector4.Lerp(from.Color.ToScaledVector4(), to.Color.ToScaledVector4(), onLocalGradient)).ToPixel<TPixel>();
            }
        }

        /// <inheritdoc />
        public override void Apply(Span<TPixel> destinationRow, ReadOnlySpan<float> scanline, int x, int y, BrushWorkspace<TPixel> workspace)
        {
            Span<float> amounts = workspace.GetAmounts(scanline.Length);
            Span<TPixel> overlays = workspace.GetOverlays(scanline.Length);

            float blendPercentage = this.Options.BlendPercentage;

            // TODO: Remove bounds checks.
            if (blendPercentage < 1)
            {
                for (int i = 0; i < scanline.Length; i++)
                {
                    amounts[i] = scanline[i] * blendPercentage;
                    overlays[i] = this[x + i, y];
                }
            }
            else
            {
                for (int i = 0; i < scanline.Length; i++)
                {
                    amounts[i] = scanline[i];
                    overlays[i] = this[x + i, y];
                }
            }

            this.blender.Blend(this.Configuration, destinationRow, destinationRow, overlays, amounts);
        }

        /// <summary>
        /// Calculates the position on the gradient for a given point.
        /// This method is abstract as it's content depends on the shape of the gradient.
        /// </summary>
        /// <param name="x">The x-coordinate of the point.</param>
        /// <param name="y">The y-coordinate of the point.</param>
        /// <returns>
        /// The position the given point has on the gradient.
        /// The position is not bound to the [0..1] interval.
        /// Values outside of that interval may be treated differently,
        /// e.g. for the <see cref="GradientRepetitionMode" /> enum.
        /// </returns>
        protected abstract float PositionOnGradient(float x, float y);

        private (ColorStop From, ColorStop To) GetGradientSegment(float positionOnCompleteGradient)
        {
            ColorStop localGradientFrom = this.colorStops[0];
            ColorStop localGradientTo = default;

            // TODO: ensure colorStops has at least 2 items (technically 1 would be okay, but that's no gradient)
            foreach (ColorStop colorStop in this.colorStops)
            {
                localGradientTo = colorStop;

                if (colorStop.Ratio > positionOnCompleteGradient)
                {
                    // we're done here, so break it!
                    break;
                }

                localGradientFrom = localGradientTo;
            }

            return (localGradientFrom, localGradientTo);
        }
    }
}
