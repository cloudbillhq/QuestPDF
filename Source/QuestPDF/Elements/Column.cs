﻿using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace QuestPDF.Elements
{
    internal sealed class ColumnItemRenderingCommand
    {
        public Element Element { get; set; }
        public SpacePlan Measurement { get; set; }
        public Size Size { get; set; }
        public Position Offset { get; set; }
    }

    internal sealed class Column : Element, ICacheable, IStateResettable
    {
        internal List<Element> Items { get; } = new();
        internal float Spacing { get; set; }
        
        private int CurrentRenderingIndex { get; set; }

        public void ResetState()
        {
            CurrentRenderingIndex = 0;
        }
        
        internal override IEnumerable<Element?> GetChildren()
        {
            return Items;
        }
        
        internal override void CreateProxy(Func<Element?, Element?> create)
        {
            Items.ForEach(x => x = create(x));
        }

        internal override SpacePlan Measure(Size availableSpace)
        {
            if (!Items.Any())
                return SpacePlan.Empty();
            
            if (CurrentRenderingIndex == Items.Count)
                return SpacePlan.Empty();
            
            var renderingCommands = PlanLayout(availableSpace);

            if (!renderingCommands.Any())
                return SpacePlan.Wrap();

            var width = renderingCommands.Max(x => x.Size.Width);
            var height = renderingCommands.Last().Offset.Y + renderingCommands.Last().Size.Height;
            var size = new Size(width, height);
            
            if (width > availableSpace.Width + Size.Epsilon || height > availableSpace.Height + Size.Epsilon)
                return SpacePlan.Wrap();
            
            if (renderingCommands.All(x => x.Measurement.Type == SpacePlanType.Empty))
                return SpacePlan.Empty();
            
            var totalRenderedItems = CurrentRenderingIndex + renderingCommands.Count(x => x.Measurement.Type is SpacePlanType.Empty or SpacePlanType.FullRender);
            var willBeFullyRendered = totalRenderedItems == Items.Count;

            return willBeFullyRendered
                ? SpacePlan.FullRender(size)
                : SpacePlan.PartialRender(size);
        }

        internal override void Draw(Size availableSpace)
        {
            var renderingCommands = PlanLayout(availableSpace);

            foreach (var command in renderingCommands)
            {
                var targetSize = new Size(availableSpace.Width, command.Size.Height);

                Canvas.Translate(command.Offset);
                command.Element.Draw(targetSize);
                Canvas.Translate(command.Offset.Reverse());
            }
            
            CurrentRenderingIndex += renderingCommands.Count(x => x.Measurement.Type is SpacePlanType.Empty or SpacePlanType.FullRender);
            
            if (CurrentRenderingIndex == Items.Count)
                ResetState();
        }

        private ICollection<ColumnItemRenderingCommand> PlanLayout(Size availableSpace)
        {
            var topOffset = 0f;
            var targetWidth = 0f;
            var commands = new List<ColumnItemRenderingCommand>();
            
            foreach (var item in Items.Skip(CurrentRenderingIndex))
            {
                var availableHeight = availableSpace.Height - topOffset;
                
                if (availableHeight < -Size.Epsilon)
                    break;

                var itemSpace = new Size(availableSpace.Width, availableHeight);
                var measurement = item.Measure(itemSpace);
                
                if (measurement.Type == SpacePlanType.Wrap)
                    break;

                // when the item does not take any space, do not add spacing
                if (measurement.Type == SpacePlanType.Empty)
                    topOffset -= Spacing;
                
                commands.Add(new ColumnItemRenderingCommand
                {
                    Element = item,
                    Size = measurement,
                    Measurement = measurement,
                    Offset = new Position(0, topOffset)
                });
                
                if (measurement.Width > targetWidth)
                    targetWidth = measurement.Width;
                
                if (measurement.Type == SpacePlanType.PartialRender)
                    break;
                
                topOffset += measurement.Height + Spacing;
            }

            foreach (var command in commands)
                command.Size = new Size(targetWidth, command.Size.Height);

            return commands;
        }
    }
}