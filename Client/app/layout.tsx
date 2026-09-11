import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Option PnL Surface",
  description: "3D option P&L surface over spot x time (Greeks frozen)",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
