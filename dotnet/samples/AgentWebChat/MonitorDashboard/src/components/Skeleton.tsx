import './Skeleton.css';

interface SkeletonProps {
  width?: string | number;
  height?: string | number;
  variant?: 'text' | 'rect' | 'circle' | 'badge';
  className?: string;
}

export function Skeleton({ width, height, variant = 'rect', className = '' }: SkeletonProps) {
  const style: React.CSSProperties = {};
  if (width) style.width = typeof width === 'number' ? `${width}px` : width;
  if (height) style.height = typeof height === 'number' ? `${height}px` : height;

  return (
    <span 
      className={`skeleton skeleton-${variant} ${className}`} 
      style={style}
      aria-hidden="true"
    />
  );
}

interface SkeletonRowProps {
  label: string;
}

export function SkeletonMetricRow({ label }: SkeletonRowProps) {
  return (
    <div className="metric">
      <span className="label">{label}</span>
      <Skeleton variant="text" width={40} height={20} />
    </div>
  );
}
