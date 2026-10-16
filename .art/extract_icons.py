"""从游戏资源里抽出原版图标（28x28 的 Sprite），存成 png，用来做生图参考图和尺寸对照。
用法: extract_icons.py 输出目录 iconCrown iconKingdom ...（不给名字就抽出 ui/Icons 下所有 icon 开头的）
需要 UnityPy；资源文件在 worldbox.app/Contents/Resources/Data/resources.assets"""
import os
import sys
import UnityPy

ASSETS = os.path.expanduser("~/Library/Application Support/Steam/steamapps/common/worldbox/worldbox.app/"
                            "Contents/Resources/Data/resources.assets")
out, names = sys.argv[1], set(sys.argv[2:])
os.makedirs(out, exist_ok=True)
for obj in UnityPy.load(ASSETS).objects:
    if obj.type.name != "Sprite": continue
    sprite = obj.read()
    if (names and sprite.m_Name in names) or (not names and sprite.m_Name.startswith("icon")):
        sprite.image.save(os.path.join(out, sprite.m_Name + ".png"))
        print(sprite.m_Name, sprite.image.size)
