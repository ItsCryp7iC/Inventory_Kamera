using System.Configuration;

namespace InventoryKamera.Properties
{
    internal sealed partial class Settings
    {
        [UserScopedSetting]
        [DefaultSettingValue("0")]
        public int ControllerConfirmLayout
        {
            get => (int)this[nameof(ControllerConfirmLayout)];
            set => this[nameof(ControllerConfirmLayout)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("True")]
        public bool UseDefaultControllerConfirmLayout
        {
            get => (bool)this[nameof(UseDefaultControllerConfirmLayout)];
            set => this[nameof(UseDefaultControllerConfirmLayout)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("0")]
        public int DeclaredOriginalInputMode
        {
            get => (int)this[nameof(DeclaredOriginalInputMode)];
            set => this[nameof(DeclaredOriginalInputMode)] = value;
        }
    }
}
