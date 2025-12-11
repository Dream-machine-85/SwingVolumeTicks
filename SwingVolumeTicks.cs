using System;
using System.Collections.Generic;
using System.Drawing;
using TradingPlatform.BusinessLayer;

namespace SwingVolumeTicks
{
    public class SwingVolumeTicks : Indicator
    {
        [InputParameter("Deviation (ticks)", 0, 1, 200, 1, 0)]
        public int DeviationTicks = 1;

        [InputParameter("Depth (bars back)", 1, 1, 50, 1, 0)]
        public int Depth = 2;

        [InputParameter("Bar Width (pixels)", 2, 1, 50, 1, 0)]
        public int BarWidthPixels = 8;

        private double tickSize;
        private double lastConfirmedPrice;
        private int lastConfirmedIndex;
        private int lastConfirmedDirection;
        private double currentExtremePrice;
        private int currentExtremeIndex;

        // Store confirmed swings for text display
        private List<SwingInfo> confirmedSwings = new List<SwingInfo>();

        // Current forming swing
        private SwingInfo formingSwing = null;

        // Track bar indices for X positioning
        private int lastConfirmedSwingBarIndex = -1;
        private int formingSwingBarIndex = -1;

        private class SwingInfo
        {
            public int BarIndex;  // Store bar index!
            public double Price;
            public double Volume;
            public int Bars;
            public bool IsHigh;
        }

        public SwingVolumeTicks()
        {
            Name = "Swing Volume Ticks";
            Description = "Tick-based ZigZag with Volume and Bar Count";

            AddLineSeries("ZigZag Up", Color.Lime, 2, LineStyle.Solid);
            AddLineSeries("ZigZag Down", Color.Red, 2, LineStyle.Solid);
            AddLineSeries("Swing Volume", Color.Yellow, 1, LineStyle.Histogramm);
            AddLineSeries("Swing Ticks", Color.Cyan, 1, LineStyle.Histogramm);
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            tickSize = Symbol?.TickSize ?? 0.25;
            lastConfirmedPrice = 0;
            lastConfirmedIndex = -1;
            lastConfirmedDirection = 0;
            currentExtremePrice = 0;
            currentExtremeIndex = -1;
            confirmedSwings.Clear();
            formingSwing = null;
        }

        protected override void OnUpdate(UpdateArgs args)
        {
            if (Count < Depth + 2)
                return;

            int currentBar = Count - 1;
            double currentHigh = High(0);
            double currentLow = Low(0);

            if (lastConfirmedIndex == -1)
            {
                lastConfirmedPrice = currentHigh;
                lastConfirmedIndex = currentBar;
                lastConfirmedDirection = 1;
                currentExtremePrice = currentLow;
                currentExtremeIndex = currentBar;
                SetValue(lastConfirmedPrice, 0, 0);
                return;
            }

            if (lastConfirmedDirection == 1)
            {
                if (currentLow < currentExtremePrice || currentExtremeIndex == -1)
                {
                    currentExtremePrice = currentLow;
                    currentExtremeIndex = currentBar;
                }
            }
            else
            {
                if (currentHigh > currentExtremePrice || currentExtremeIndex == -1)
                {
                    currentExtremePrice = currentHigh;
                    currentExtremeIndex = currentBar;
                }
            }

            bool extremeConfirmed = false;

            if (currentBar - currentExtremeIndex >= Depth)
            {
                double changeInTicks = Math.Abs((currentExtremePrice - lastConfirmedPrice) / tickSize);
                if (changeInTicks >= DeviationTicks)
                    extremeConfirmed = true;
            }

            if (extremeConfirmed)
            {
                double legVolume = 0;
                int legBars = currentExtremeIndex - lastConfirmedIndex;
                for (int i = lastConfirmedIndex + 1; i <= currentExtremeIndex; i++)
                {
                    int offset = Count - 1 - i;
                    if (offset >= 0 && offset < Count)
                        legVolume += Volume(offset);
                }

                bool isHigh = (lastConfirmedDirection == -1);

                // Calculate tick distance (price change / tick size)
                int tickDistance = (int)Math.Round((currentExtremePrice - lastConfirmedPrice) / tickSize);

                DrawLineBetweenPivots(lastConfirmedIndex, lastConfirmedPrice,
                                      currentExtremeIndex, currentExtremePrice);

                int pivotOffset = Count - 1 - currentExtremeIndex;
                if (pivotOffset >= 0)
                {
                    SetValue(legVolume, 2, pivotOffset);
                    SetValue(tickDistance, 3, pivotOffset); // Store tick distance instead of bars

                    if (isHigh)
                    {
                        LinesSeries[0].SetMarker(pivotOffset,
                            new IndicatorLineMarker(Color.Lime,
                                upperIcon: IndicatorLineMarkerIconType.UpArrow));
                    }
                    else
                    {
                        LinesSeries[1].SetMarker(pivotOffset,
                            new IndicatorLineMarker(Color.Red,
                                bottomIcon: IndicatorLineMarkerIconType.DownArrow));
                    }
                }

                // Store confirmed swing for text display
                confirmedSwings.Add(new SwingInfo
                {
                    BarIndex = currentExtremeIndex, // Store bar index!
                    Price = currentExtremePrice,
                    Volume = legVolume,
                    Bars = tickDistance,
                    IsHigh = isHigh
                });

                lastConfirmedSwingBarIndex = currentExtremeIndex;

                // Keep only recent swings (last 50) to avoid memory issues
                if (confirmedSwings.Count > 50)
                    confirmedSwings.RemoveAt(0);

                lastConfirmedPrice = currentExtremePrice;
                lastConfirmedIndex = currentExtremeIndex;
                lastConfirmedDirection = -lastConfirmedDirection;

                if (lastConfirmedDirection == 1)
                {
                    currentExtremePrice = currentLow;
                    currentExtremeIndex = currentBar;
                }
                else
                {
                    currentExtremePrice = currentHigh;
                    currentExtremeIndex = currentBar;
                }

                formingSwing = null;
            }
            else
            {
                // Update forming swing (real-time)
                double formingVolume = 0;
                for (int i = lastConfirmedIndex + 1; i <= currentBar; i++)
                {
                    int offset = Count - 1 - i;
                    if (offset >= 0 && offset < Count)
                        formingVolume += Volume(offset);
                }

                // Calculate tick distance for forming swing
                int formingTickDistance = (int)Math.Round((currentExtremePrice - lastConfirmedPrice) / tickSize);

                formingSwing = new SwingInfo
                {
                    BarIndex = currentExtremeIndex, // Store bar index!
                    Price = currentExtremePrice,
                    Volume = formingVolume,
                    Bars = formingTickDistance, // Tick distance, not bars
                    IsHigh = (lastConfirmedDirection == -1)
                };

                formingSwingBarIndex = currentExtremeIndex;
            }
        }

        private void DrawLineBetweenPivots(int fromIdx, double fromPrice, int toIdx, double toPrice)
        {
            int bars = toIdx - fromIdx;
            if (bars <= 0) return;

            double step = (toPrice - fromPrice) / bars;
            int lineIndex = (toPrice > fromPrice) ? 0 : 1;

            for (int i = 0; i <= bars; i++)
            {
                int barIdx = fromIdx + i;
                int offset = Count - 1 - barIdx;
                if (offset >= 0 && offset < Count)
                    SetValue(fromPrice + (step * i), lineIndex, offset);
            }
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            Graphics gr = args.Graphics;
            Font font = new Font("Arial", 9, FontStyle.Bold);

            // Get price range for Y positioning
            double minPrice = double.MaxValue;
            double maxPrice = double.MinValue;

            int lookback = Math.Min(Count, 200);
            for (int i = 0; i < lookback; i++)
            {
                double h = High(i);
                double l = Low(i);
                if (h > maxPrice) maxPrice = h;
                if (l < minPrice) minPrice = l;
            }

            if (maxPrice <= minPrice)
            {
                font.Dispose();
                return;
            }

            double priceRange = maxPrice - minPrice;

            // Only draw the MOST RECENT confirmed swing (WHITE)
            if (confirmedSwings.Count > 0)
            {
                var lastSwing = confirmedSwings[confirmedSwings.Count - 1];

                double pricePct = (maxPrice - lastSwing.Price) / priceRange;
                float y = args.Rectangle.Top + (float)(pricePct * args.Rectangle.Height);

                // Position near right edge but not at the edge
                float x = args.Rectangle.Right - 150;

                string volText = (lastSwing.Volume / 1000.0).ToString("0.#") + "K";
                string tickText = (lastSwing.Bars >= 0 ? "+" : "") + lastSwing.Bars.ToString(); // Show +/- sign

                if (lastSwing.IsHigh)
                {
                    gr.DrawString(volText, font, Brushes.White, x, y - 30);
                    gr.DrawString(tickText, font, Brushes.White, x, y - 15);
                }
                else
                {
                    gr.DrawString(volText, font, Brushes.White, x, y + 5);
                    gr.DrawString(tickText, font, Brushes.White, x, y + 20);
                }
            }

            // Draw forming swing (YELLOW text - real-time)
            if (formingSwing != null && formingSwing.Bars != 0)
            {
                double pricePct = (maxPrice - formingSwing.Price) / priceRange;
                float y = args.Rectangle.Top + (float)(pricePct * args.Rectangle.Height);

                // Position near right edge
                float x = args.Rectangle.Right - 150;

                string volText = (formingSwing.Volume / 1000.0).ToString("0.#") + "K";
                string tickText = (formingSwing.Bars >= 0 ? "+" : "") + formingSwing.Bars.ToString(); // Show +/- sign

                if (formingSwing.IsHigh)
                {
                    gr.DrawString(volText, font, Brushes.Yellow, x, y - 30);
                    gr.DrawString(tickText, font, Brushes.Yellow, x, y - 15);
                }
                else
                {
                    gr.DrawString(volText, font, Brushes.Yellow, x, y + 5);
                    gr.DrawString(tickText, font, Brushes.Yellow, x, y + 20);
                }
            }

            font.Dispose();
        }
    }
}