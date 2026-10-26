"""Encode Unity's sampled sword-animation renders using Blender's native sequencer."""
import bpy
import sys
from pathlib import Path

root=Path(__file__).resolve().parents[1]
running='--run' in sys.argv
frames=sorted((root/'Temp/sword-run').glob('Run-[0-9][0-9][0-9].png')) if running else sorted((root/'Temp/sword-pro').glob('Draw-[0-9][0-9][0-9].png'))
if not running: frames+=sorted((root/'Temp/sword-pro').glob('Sheathe-[0-9][0-9][0-9].png'))
assert len(frames)==144, f'Expected both complete 72-frame actions, got {len(frames)}'
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
editor=scene.sequence_editor_create()
strip=editor.strips.new_image('Tanjiro sword running' if running else 'Tanjiro sword draw and sheathe',str(frames[0]),channel=1,frame_start=1)
for frame in frames[1:]: strip.elements.append(frame.name)
strip.frame_final_duration=len(frames)
scene.frame_start=1;scene.frame_end=len(frames)
scene.render.resolution_x=720;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.render.fps=24
scene.view_settings.view_transform='Standard'
scene.render.image_settings.media_type='VIDEO'
scene.render.image_settings.file_format='FFMPEG'
scene.render.ffmpeg.format='MPEG4';scene.render.ffmpeg.codec='H264'
scene.render.ffmpeg.constant_rate_factor='MEDIUM'
scene.render.filepath=str(root/('SourceArt/Characters/Tanjiro-SwordRunning-ForwardElbow24fps.mp4' if running else 'SourceArt/Characters/Tanjiro-SwordActions-Polished24fps.mp4'))
bpy.ops.render.render(animation=True)
print('SWORD PREVIEW',scene.render.filepath)
