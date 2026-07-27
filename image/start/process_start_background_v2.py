from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent
GENERATED = Path(
    "/Users/dh080/.codex/generated_images/019fa286-d229-71e3-91eb-fb3fafcc103e/"
    "exec-597980f3-c9d4-4b58-8364-ddfc65b7ed57.png"
)


def fit(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    result = image.copy()
    result.thumbnail(size, Image.Resampling.LANCZOS)
    return result


def place_center(
    canvas: Image.Image,
    image: Image.Image,
    center_x: int,
    top: int,
    max_size: tuple[int, int],
) -> None:
    item = fit(image, max_size)
    canvas.alpha_composite(item, (center_x - item.width // 2, top))


def main() -> None:
    generated = Image.open(GENERATED).convert("RGB")
    background = generated.resize((2560, 1440), Image.Resampling.LANCZOS)
    background.save(ROOT / "start_background_v2.png", optimize=True)

    preview = background.convert("RGBA")
    logo = Image.open(ROOT / "logo_ugauga_game.png").convert("RGBA")
    place_center(preview, logo, 1280, 30, (1580, 550))

    buttons = [
        "button_game_start.png",
        "button_how_to_play.png",
        "button_settings.png",
        "button_exit.png",
    ]
    positions = [(820, 710), (1740, 710), (820, 1070), (1740, 1070)]
    for filename, (center_x, top) in zip(buttons, positions):
        button = Image.open(ROOT / filename).convert("RGBA")
        place_center(preview, button, center_x, top, (760, 310))

    preview.convert("RGB").save(ROOT / "start_screen_preview_v2.png", quality=95)


if __name__ == "__main__":
    main()
