# UnlockServer
UnlockServer 一个蓝牙设备解锁锁定电脑的小工具

更新说明：
v1.4：
1.添加锁屏超时（指定时间内最多锁屏一次，默认60秒）
2.添加解锁超时（指定时间内最多解锁一次，默认30秒）

软件介绍：
本软件是一款蓝牙自动锁屏和解锁软件,需要配合远程解锁软件使用，相关软件我会打包发到蓝奏云。
本软件可以根据你设置的蓝牙设备的信号强度，自动锁定和解锁电脑，可以选择是否干预人工锁定或者解锁（手动锁定后不自动解锁或锁屏）。

使用说明：
下载安装后，再安装远程解锁软件，然后只需要设置用户名密码（本地计算机用户名密码），然后点击刷新蓝牙设备（如果查找不到，请检查是否开启设备的蓝牙发现功能，不行的话换BLE设备再刷新几次），选择你的蓝牙设备然后设置信号阈值（默认为-90，越小越容易被解锁）然后点击保存配置即可，软件自带的锁屏功能，视为软件锁屏，无视不干预人工锁定复选框。

常见问题：
由于某些蓝牙设备配对后获取不到rssi信号值，所以可能需要解除配对然后开启设备发现才可以使用（本人测试小米和华为手环在连接了手机之后电脑是发现不了的，需要断开和手机的连接才能发现手环设备，安卓10以上系统的手机也是默认发现不了的，可能需要插件开启一直打开蓝牙发现功能）


软件下载地址：
https://wwt.lanzouy.com/ieds50a0g7gf
密码:52pj

![505e49d2740377bc99843402900653aa_085004l90qqxd09dxxqgxl](https://user-images.githubusercontent.com/18580281/185857388-5cab0b72-f0fe-4b02-99c5-3190fc666548.png)
