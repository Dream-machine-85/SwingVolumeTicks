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

        private double tickSize;
        private double lastConfirmedPrice;
        private int lastConfirmedIndex;
        private int lastConfirmedDirection;
        private double currentExtremePrice;
        private int currentExtremeIndex;

        private List<SwingInfo> confirmedSwings = new List<SwingInfo>();
        private SwingInfo formingSwing = null;
        private int formingSwingBarIndex = -1;

        private class SwingInfo
        {
            public int BarIndex;
            public double Price;
            public double Volume;
            public int Ticks;
            public bool IsHigh;
        }

        public SwingVolumeTicks()
        {
            Name = "Swing Volume Ticks";
            Description = "Tick-based ZigZag with Volume and Tick Count";

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
            if (this.Count < Depth + 2)
                return;

            int currentBar = this.Count - 1;
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
                for (int i = lastConfirmedIndex + 1; i <= currentExtremeIndex; i++)
                {
                    int offset = this.Count - 1 - i;
                    if (offset >= 0 && offset < this.Count)
                        legVolume += Volume(offset);
                }

                bool isHigh = (lastConfirmedDirection == -1);
                int tickDistance = (int)Math.Round((currentExtremePrice - lastConfirmedPrice) / tickSize);

                DrawLineBetweenPivots(lastConfirmedIndex, lastConfirmedPrice,
                                      currentExtremeIndex, currentExtremePrice);

                int pivotOffset = this.Count - 1 - currentExtremeIndex;
                if (pivotOffset >= 0)
                {
                    SetValue(legVolume, 2, pivotOffset);
                    SetValue(tickDistance, 3, pivotOffset);

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

                confirmedSwings.Add(new SwingInfo
                {
                    BarIndex = currentExtremeIndex,
                    Price = currentExtremePrice,
                    Volume = legVolume,
                    Ticks = tickDistance,
                    IsHigh = isHigh
                });

                if (confirmedSwings.Count > 100)
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
                double formingVolume = 0;
                for (int i = lastConfirmedIndex + 1; i <= currentBar; i++)
                {
                    int offset = this.Count - 1 - i;
                    if (offset >= 0 && offset < this.Count)
                        formingVolume += Volume(offset);
                }

                int formingTickDistance = (int)Math.Round((currentExtremePrice - lastConfirmedPrice) / tickSize);

                formingSwing = new SwingInfo
                {
                    BarIndex = currentExtremeIndex,
                    Price = currentExtremePrice,
                    Volume = formingVolume,
                    Ticks = formingTickDistance,
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
                int offset = this.Count - 1 - barIdx;
                if (offset >= 0 && offset < this.Count)
                    SetValue(fromPrice + (step * i), lineIndex, offset);
            }
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            if (this.CurrentChart?.MainWindow?.CoordinatesConverter == null)
                return;

            var converter = this.CurrentChart.MainWindow.CoordinatesConverter;
            Graphics gr = args.Graphics;
            Font font = new Font("Arial", 12, FontStyle.Bold);

            /// DRAW ALL CONFIRMED SWINGS (WHITE TEXT)
            foreach (var swing in confirmedSwings)
            {
                /// Get the bar time
                var barTime = Time(this.Count - 1 - swing.BarIndex);

                //// Convert to pixel coordinates using official API finally
                double x = converter.GetChartX(barTime);
                double y = converter.GetChartY(swing.Price);

                if (x < args.Rectangle.Left - 100 || x > args.Rectangle.Right + 100)
                    continue;

                string volText = (swing.Volume / 1000.0).ToString("0.#") + "K";
                string tickText = (swing.Ticks >= 0 ? "+" : "") + swing.Ticks.ToString();

                if (swing.IsHigh)
                {
                    gr.DrawString(volText, font, Brushes.White, (float)x + 5, (float)y - 50);
                    gr.DrawString(tickText, font, Brushes.White, (float)x + 5, (float)y - 30);
                }
                else
                {
                    gr.DrawString(volText, font, Brushes.White, (float)x + 5, (float)y + 10);
                    gr.DrawString(tickText, font, Brushes.White, (float)x + 5, (float)y + 30);
                }
            }

            /// DRAW FORMING SWING (YELLOW TEXT)
            if (formingSwing != null && formingSwing.Ticks != 0)
            {
                var barTime = Time(this.Count - 1 - formingSwing.BarIndex);

                double x = converter.GetChartX(barTime);
                double y = converter.GetChartY(formingSwing.Price);

                string volText = (formingSwing.Volume / 1000.0).ToString("0.#") + "K";
                string tickText = (formingSwing.Ticks >= 0 ? "+" : "") + formingSwing.Ticks.ToString();

                if (formingSwing.IsHigh)
                {
                    gr.DrawString(volText, font, Brushes.Yellow, (float)x + 5, (float)y - 50);
                    gr.DrawString(tickText, font, Brushes.Yellow, (float)x + 5, (float)y - 30);
                }
                else
                {
                    gr.DrawString(volText, font, Brushes.Yellow, (float)x + 5, (float)y + 10);
                    gr.DrawString(tickText, font, Brushes.Yellow, (float)x + 5, (float)y + 30);
                }
            }

            font.Dispose();
        }
    }
}