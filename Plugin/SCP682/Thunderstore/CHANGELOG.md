## 1.0.1

### Fixed

- Fixed an issue where 682 wouldn't spawn when the apparatus was pulled with specific mods installed, even when in some modpacks with those exact mods this wasn't an issue. This happened when the following conditions were true:

  - `LungProp.EquipItem` was hooked before 682 hooked `LungProp.DisconnectFromMachinery`
  - no mod hooked `LungProp.EquipItem` after 682 hooked `LungProp.DisconnectFromMachinery`

> The above issue happened because of inlining, where the `LungProp.DisconnectFromMachinery` is so small that the JIT compiler decides to essentially copy paste that method into the `LungProp.EquipItem` method instead of calling that method normally.
>
> Without any mods hooking the `LungProp.EquipItem`, the method wouldn't get compiled before 682 hooks `LungProp.DisconnectFromMachinery`, so it would copy paste 682's hooked method instead, as we want.
>
> The issue was fixed by making 682 force a recompilation of the `LungProp.EquipItem` method by applying an empty ILHook on it after hooking `LungProp.DisconnectFromMachinery` .

## 1.0.0

- Release on Thunderstore.
