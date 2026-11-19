import { lazy, type ComponentType, type LazyExoticComponent } from "react";

export interface ScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type ScreenLoader = LazyExoticComponent<ComponentType<ScreenProps>>;

/**
 * Screen registry. Every screen is lazy-loaded so the first window opens fast; features add their screen here.
 * One implementation per workflow: a screen id maps to exactly one component.
 */
export const screens: Readonly<Record<string, ScreenLoader>> = {
  home: lazy(() => import("@/features/home/HomeScreen")),
  // Development gallery of the input engine (opened from the header in dev builds only).
  gallery: lazy(() => import("@/features/gallery/GalleryScreen")),
};
