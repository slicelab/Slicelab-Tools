using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Slicelab.Utilities
{
    public class CounterComponent : GH_Component
    {
        private double _storedValue;
        private bool _stopped;
        private bool _initialized;

        public CounterComponent()
            : base("Counter", "SLCount",
                "Stateful counter that increments each solve. Toggle Run to self-timer, or attach an external Timer. Stops at max value.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override Guid ComponentGuid => new Guid("8B9C0D1E-2F3A-4B4C-5D6E-7F8091020308");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Count.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Start", "S", "Starting value", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Increment", "I", "Step size per solve", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Max Value", "M", "Stop incrementing after this value", GH_ParamAccess.item, 100.0);
            pManager.AddBooleanParameter("Run", "R", "Start/stop the self-timer", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Interval", "Int", "Milliseconds between increments", GH_ParamAccess.item, 100);
            pManager.AddBooleanParameter("Reset", "Rst", "Reset counter to start", GH_ParamAccess.item, false);
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();

            // Run — Boolean Toggle (input 3)
            if (Params.Input[3].SourceCount == 0)
            {
                var grip = Params.Input[3].Attributes.InputGrip;
                var toggle = new GH_BooleanToggle();
                toggle.CreateAttributes();
                toggle.NickName = "Run";
                toggle.Value = false;
                toggle.Attributes.PerformLayout();
                IconHelper.AlignWidget(toggle, grip);
                document.AddObject(toggle, false);
                Params.Input[3].AddSource(toggle);
            }

            // Interval — Value List dropdown (input 4)
            if (Params.Input[4].SourceCount == 0)
            {
                var grip = Params.Input[4].Attributes.InputGrip;
                var vl = new GH_ValueList();
                vl.CreateAttributes();
                vl.ListMode = GH_ValueListMode.DropDown;
                vl.NickName = "Interval";
                vl.ListItems.Clear();
                vl.ListItems.Add(new GH_ValueListItem("50 ms", "50"));
                vl.ListItems.Add(new GH_ValueListItem("100 ms", "100"));
                vl.ListItems.Add(new GH_ValueListItem("250 ms", "250"));
                vl.ListItems.Add(new GH_ValueListItem("500 ms", "500"));
                vl.ListItems.Add(new GH_ValueListItem("1 sec", "1000"));
                vl.ListItems.Add(new GH_ValueListItem("2 sec", "2000"));
                vl.ListItems.Add(new GH_ValueListItem("5 sec", "5000"));
                vl.ListItems.Add(new GH_ValueListItem("10 sec", "10000"));
                // Default select 100 ms (index 1)
                vl.SelectItem(1);
                vl.Attributes.PerformLayout();
                IconHelper.AlignWidget(vl, grip);
                document.AddObject(vl, false);
                Params.Input[4].AddSource(vl);
            }

            // Reset — Button (input 5)
            if (Params.Input[5].SourceCount == 0)
            {
                var grip = Params.Input[5].Attributes.InputGrip;
                var button = new GH_ButtonObject();
                button.CreateAttributes();
                button.NickName = "Reset";
                button.Attributes.PerformLayout();
                IconHelper.AlignWidget(button, grip);
                document.AddObject(button, false);
                Params.Input[5].AddSource(button);
            }
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Value", "V", "Current counter value", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Active", "A", "True while counter is still incrementing", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double start = 0;
            double increment = 1;
            double maxValue = 100;
            bool run = false;
            int interval = 100;
            bool reset = false;

            DA.GetData(0, ref start);
            DA.GetData(1, ref increment);
            DA.GetData(2, ref maxValue);
            DA.GetData(3, ref run);
            DA.GetData(4, ref interval);
            DA.GetData(5, ref reset);

            // Clamp interval to prevent freezing
            if (interval < 10) interval = 10;

            if (reset)
            {
                _storedValue = start;
                _stopped = false;
                _initialized = true;
                DA.SetData(0, _storedValue);
                DA.SetData(1, true);
                // If run is also true, keep going after reset
                if (run)
                    OnPingDocument()?.ScheduleSolution(interval, doc => ExpireSolution(false));
                return;
            }

            if (!_initialized)
            {
                _storedValue = start;
                _stopped = false;
                _initialized = true;
                DA.SetData(0, _storedValue);
                DA.SetData(1, true);
                if (run)
                    OnPingDocument()?.ScheduleSolution(interval, doc => ExpireSolution(false));
                return;
            }

            if (_stopped)
            {
                DA.SetData(0, _storedValue);
                DA.SetData(1, false);
                return;
            }

            // Only increment when Run is true (self-timer or external timer with Run enabled)
            if (!run)
            {
                DA.SetData(0, _storedValue);
                DA.SetData(1, !_stopped);
                return;
            }

            _storedValue += increment;

            if (_storedValue >= maxValue)
            {
                _storedValue = maxValue;
                _stopped = true;
            }

            DA.SetData(0, _storedValue);
            DA.SetData(1, !_stopped);

            // Schedule next solve if running and not stopped
            if (!_stopped && run)
                OnPingDocument()?.ScheduleSolution(interval, doc => ExpireSolution(false));
        }
    }
}
