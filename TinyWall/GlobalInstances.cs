using System;
using System.Drawing;
using System.Diagnostics.CodeAnalysis;
using pylorak.TinyWall.DatabaseClasses;

namespace pylorak.TinyWall
{
    internal static class GlobalInstances
    {
        [AllowNull]
        internal static AppDatabase AppDatabase;
        [AllowNull]
        internal static Controller Controller;
        internal static Guid ClientChangeset;
        internal static Guid ServerChangeset;

        public static void InitClient()
        {
            Controller ??= new Controller("TinyWallController");
        }

        [AllowNull]
        private static Bitmap _ApplyBtnIcon = null;
        internal static Bitmap ApplyBtnIcon
        {
            get
            {
                if (null == _ApplyBtnIcon)
                    _ApplyBtnIcon = UiUtils.ScaleImage(Resources.Icons.accept, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _ApplyBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _CancelBtnIcon = null;
        internal static Bitmap CancelBtnIcon
        {
            get
            {
                if (null == _CancelBtnIcon)
                    _CancelBtnIcon = UiUtils.ScaleImage(Resources.Icons.cancel, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _CancelBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _UninstallBtnIcon = null;
        internal static Bitmap UninstallBtnIcon
        {
            get
            {
                if (null == _UninstallBtnIcon)
                    _UninstallBtnIcon = UiUtils.ScaleImage(Resources.Icons.uninstall, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _UninstallBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _AddBtnIcon = null;
        internal static Bitmap AddBtnIcon
        {
            get
            {
                if (null == _AddBtnIcon)
                    _AddBtnIcon = UiUtils.ScaleImage(Resources.Icons.add, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _AddBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _ModifyBtnIcon = null;
        internal static Bitmap ModifyBtnIcon
        {
            get
            {
                if (null == _ModifyBtnIcon)
                    _ModifyBtnIcon = UiUtils.ScaleImage(Resources.Icons.modify, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _ModifyBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _RemoveBtnIcon = null;
        internal static Bitmap RemoveBtnIcon
        {
            get
            {
                if (null == _RemoveBtnIcon)
                    _RemoveBtnIcon = UiUtils.ScaleImage(Resources.Icons.remove, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _RemoveBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _SubmitBtnIcon = null;
        internal static Bitmap SubmitBtnIcon
        {
            get
            {
                if (null == _SubmitBtnIcon)
                    _SubmitBtnIcon = UiUtils.ScaleImage(Resources.Icons.submit, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _SubmitBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _ImportBtnIcon = null;
        internal static Bitmap ImportBtnIcon
        {
            get
            {
                if (null == _ImportBtnIcon)
                    _ImportBtnIcon = UiUtils.ScaleImage(Resources.Icons.import, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _ImportBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _ExportBtnIcon = null;
        internal static Bitmap ExportBtnIcon
        {
            get
            {
                if (null == _ExportBtnIcon)
                    _ExportBtnIcon = UiUtils.ScaleImage(Resources.Icons.export, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _ExportBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _UpdateBtnIcon = null;
        internal static Bitmap UpdateBtnIcon
        {
            get
            {
                if (null == _UpdateBtnIcon)
                    _UpdateBtnIcon = UiUtils.ScaleImage(Resources.Icons.update, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _UpdateBtnIcon;
            }
        }

        [AllowNull]
        private static Bitmap _WebBtnIcon = null;
        internal static Bitmap WebBtnIcon
        {
            get
            {
                if (null == _WebBtnIcon)
                    _WebBtnIcon = UiUtils.ScaleImage(Resources.Icons.web, UiUtils.DpiScalingFactor, UiUtils.DpiScalingFactor);

                return _WebBtnIcon;
            }
        }
    }
}
