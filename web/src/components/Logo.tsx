export function Logo({ size = 30 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 32 32"
      fill="none"
      aria-hidden="true"
      className="logo"
    >
      <path
        d="M16 2.5 27.5 9.2v13.6L16 29.5 4.5 22.8V9.2L16 2.5Z"
        stroke="currentColor"
        strokeWidth="1.2"
        strokeLinejoin="round"
      />
      <path
        d="M17.6 3.4 13.4 12l5.6 4.1-4.8 7.4 3.4 4.6"
        stroke="var(--amber)"
        strokeWidth="1.4"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx="19" cy="16.1" r="1.6" fill="var(--amber)" />
    </svg>
  )
}
